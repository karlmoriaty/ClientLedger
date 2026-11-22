using System.IO;
using ClientLedger.Data;
using ClientLedger.Data.Audit;
using ClientLedger.Foundation.Platform;
using ClientLedger.Licensing;
using ClientLedger.Modules.Crm.Data;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;
using Microsoft.Data.Sqlite;

namespace ClientLedger.Tests;

public sealed class DatabaseTests
{
    [Fact]
    public async Task InitializeAsync_CreatesPlatformAndCrmTables()
    {
        using var scope = new TestScope();
        var (factory, initializer) = scope.CreateDatabase();

        await initializer.InitializeAsync();

        await using var connection = await factory.OpenAsync();

        Assert.Equal(1L, await CountAsync(connection, "SELECT COUNT(1) FROM platform_schema_version WHERE module_id = 'platform';"));
        Assert.Equal(1L, await CountAsync(connection, "SELECT COUNT(1) FROM platform_schema_version WHERE module_id = 'crm';"));
        Assert.Equal(1L, await CountAsync(connection, "SELECT COUNT(1) FROM sqlite_master WHERE type = 'table' AND name = 'crm_quote_line';"));
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        using var scope = new TestScope();
        var (factory, initializer) = scope.CreateDatabase();

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        await using var connection = await factory.OpenAsync();
        Assert.Equal(1L, await CountAsync(connection, "SELECT COUNT(1) FROM platform_schema_version WHERE module_id = 'crm' AND version = 1;"));
    }

    [Fact]
    public async Task SaveAsync_WritesAuditInSameTransaction()
    {
        using var scope = new TestScope();
        var (factory, initializer) = scope.CreateDatabase();
        await initializer.InitializeAsync();

        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var identity = new TestIdentity();
        var settings = new SettingsStore(factory, clock);
        var unitOfWork = new SqliteUnitOfWork(factory);
        var audit = new AuditStore(factory, identity, clock);
        var userContext = new UserContext(settings, identity);
        var permissions = new PermissionService(userContext);

        var store = new CustomerStore(unitOfWork, factory, permissions, userContext, audit, clock);

        var id = await store.SaveAsync(new Customer { Id = "cus_test_1", Name = "测试客户" });

        var customer = await store.GetAsync(id);
        Assert.NotNull(customer);
        Assert.Equal("测试客户", customer!.Name);
        Assert.Equal(identity.Actor, customer.Owner);

        var records = await audit.RecentAsync(10);
        Assert.Contains(records, r => r.Action == "crm.customer.create" && r.EntityId == id);
    }

    [Fact]
    public async Task StaffScope_OnlySeesOwnedRecords()
    {
        using var scope = new TestScope();
        var (factory, initializer) = scope.CreateDatabase();
        await initializer.InitializeAsync();

        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var identity = new TestIdentity();
        var settings = new SettingsStore(factory, clock);
        var unitOfWork = new SqliteUnitOfWork(factory);
        var audit = new AuditStore(factory, identity, clock);
        var userContext = new UserContext(settings, identity);
        var permissions = new PermissionService(userContext);
        var store = new CustomerStore(unitOfWork, factory, permissions, userContext, audit, clock);

        await store.SaveAsync(new Customer { Id = "cus_mine", Name = "我的客户" });
        await store.SaveAsync(new Customer { Id = "cus_other", Name = "别人的客户", Owner = @"TEST\other" });

        var asOwner = await store.ListAsync(new CustomerQuery());
        Assert.Equal(2, asOwner.Count);

        await userContext.SwitchRoleAsync(UserRole.Staff);

        var asStaff = await store.ListAsync(new CustomerQuery());
        Assert.Single(asStaff);
        Assert.Equal("cus_mine", asStaff[0].Id);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletesCustomerAndCascadesContacts()
    {
        using var scope = new TestScope();
        var (factory, initializer) = scope.CreateDatabase();
        await initializer.InitializeAsync();

        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var identity = new TestIdentity();
        var settings = new SettingsStore(factory, clock);
        var unitOfWork = new SqliteUnitOfWork(factory);
        var audit = new AuditStore(factory, identity, clock);
        var userContext = new UserContext(settings, identity);
        var permissions = new PermissionService(userContext);
        var store = new CustomerStore(unitOfWork, factory, permissions, userContext, audit, clock);
        var contacts = new ContactStore(unitOfWork, factory, permissions, userContext, audit, clock);

        await store.SaveAsync(new Customer { Id = "cus_del", Name = "待删除" });
        await contacts.SaveAsync(new Contact { Id = "ct_1", CustomerId = "cus_del", Name = "联系人" });

        await store.DeleteAsync("cus_del");

        Assert.Empty(await store.ListAsync(new CustomerQuery()));
        await using var connection = await factory.OpenAsync();
        Assert.Equal(1L, await CountAsync(connection, "SELECT COUNT(1) FROM crm_contact WHERE id = 'ct_1' AND is_deleted = 1;"));
    }

    private static async Task<long> CountAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}

/// <summary>每个测试独立目录，避免相互污染；结束时清空连接池再删目录。</summary>
internal sealed class TestScope : IDisposable
{
    public TestScope()
    {
        Root = Path.Combine(Path.GetTempPath(), "clientledger-tests", Guid.NewGuid().ToString("N"));
        Paths = new AppPaths(Root, Path.Combine(Root, "shared"));
        Log = new FileAppLog(Paths.LogDirectory);
    }

    public string Root { get; }

    public AppPaths Paths { get; }

    public IAppLog Log { get; }

    public (SqliteConnectionFactory Factory, DatabaseInitializer Initializer) CreateDatabase()
    {
        var clock = SystemClock.Instance;
        var factory = new SqliteConnectionFactory(Paths.DatabaseFilePath, Log);
        var initializer = new DatabaseInitializer(
            factory,
            [new PlatformMigrationSource(), new CrmMigrationSource()],
            clock,
            Log);

        return (factory, initializer);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
        catch
        {
            // 测试临时目录清理失败不影响测试结论。
        }
    }
}

internal sealed class TestIdentity : IIdentityProvider
{
    public string Actor { get; init; } = @"TEST\owner";

    public string UserSid => "S-1-5-21-0000-0000-0000-1001";

    public string MachineName => "TEST-MACHINE";

    public string MachineFingerprint => "0123456789ABCDEF";
}