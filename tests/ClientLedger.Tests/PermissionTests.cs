using ClientLedger.Security;

namespace ClientLedger.Tests;

public sealed class PermissionTests
{
    [Theory]
    [InlineData(UserRole.Owner, Permissions.CrmCustomerDelete, true)]
    [InlineData(UserRole.Manager, Permissions.CrmCustomerDelete, true)]
    [InlineData(UserRole.Staff, Permissions.CrmCustomerDelete, false)]
    [InlineData(UserRole.ReadOnly, Permissions.CrmCustomerWrite, false)]
    [InlineData(UserRole.Staff, Permissions.CrmCustomerWrite, true)]
    [InlineData(UserRole.Manager, Permissions.PlatformLicenseManage, false)]
    [InlineData(UserRole.Owner, Permissions.PlatformLicenseManage, true)]
    [InlineData(UserRole.Manager, Permissions.PlatformAuditRead, true)]
    [InlineData(UserRole.Staff, Permissions.PlatformAuditRead, false)]
    public void RoleCatalog_GrantsExpectedPermission(UserRole role, string permission, bool expected)
    {
        Assert.Equal(expected, RoleCatalog.PermissionsFor(role).Contains(permission));
    }

    [Theory]
    [InlineData(UserRole.Owner, DataScope.All)]
    [InlineData(UserRole.Manager, DataScope.All)]
    [InlineData(UserRole.Staff, DataScope.Owned)]
    [InlineData(UserRole.ReadOnly, DataScope.All)]
    public void RoleCatalog_MapsDataScope(UserRole role, DataScope expected)
    {
        Assert.Equal(expected, RoleCatalog.ScopeFor(role));
    }

    [Fact]
    public void PermissionService_DemandThrowsForMissingPermission()
    {
        var context = new FakeUserContext(RoleCatalog.PermissionsFor(UserRole.Staff), DataScope.Owned);
        var service = new PermissionService(context);

        Assert.True(service.Has(Permissions.CrmCustomerRead));
        Assert.Throws<UnauthorizedAccessException>(() => service.Demand(Permissions.CrmCustomerDelete));
    }

    [Fact]
    public void AllPermissions_HaveChineseDescription()
    {
        foreach (var permission in Permissions.All)
        {
            var text = Permissions.Describe(permission);
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.NotEqual(permission, text);
        }
    }

    private sealed class FakeUserContext(IReadOnlySet<string> permissions, DataScope scope) : IUserContext
    {
        public UserSession Current { get; } = new("TEST\\user", "S-1", UserRole.Staff, scope, permissions);

        public event EventHandler? Changed
        {
            add { }
            remove { }
        }

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SwitchRoleAsync(UserRole role, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}