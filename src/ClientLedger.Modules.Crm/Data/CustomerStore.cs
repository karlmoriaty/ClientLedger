using ClientLedger.Data;
using ClientLedger.Data.Audit;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;
using Microsoft.Data.Sqlite;

namespace ClientLedger.Modules.Crm.Data;

public sealed record CustomerQuery
{
    public string? Search { get; init; }

    public CustomerStatus? Status { get; init; }

    public CustomerGrade? Grade { get; init; }

    public bool OnlyMine { get; init; }
}

public sealed record CustomerStats(
    int Total,
    int NewThisMonth,
    int PendingFollowUps,
    int WonThisMonth);

/// <summary>
/// 客户写入的唯一入口。写操作一律：权限校验 → 单事务内写业务表 + 写审计 → 提交。
/// 读操作按会话数据范围过滤，不做"先查全量再在内存里筛"的危险写法。
/// </summary>
public sealed class CustomerStore(
    IUnitOfWork unitOfWork,
    IDbConnectionFactory factory,
    IPermissionService permissions,
    IUserContext userContext,
    IAuditWriter audit,
    IClock clock)
{
    private const string Columns =
        "id, name, industry, scale, source, grade, status, owner, phone, email, website, address, tags, note, created_utc, updated_utc";

    public async Task<IReadOnlyList<Customer>> ListAsync(CustomerQuery query, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmCustomerRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var conditions = new List<string> { "is_deleted = 0" };
        var parameters = new List<(string, object?)>();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("(name LIKE $kw ESCAPE '\\' OR industry LIKE $kw ESCAPE '\\' OR tags LIKE $kw ESCAPE '\\' OR phone LIKE $kw ESCAPE '\\')");
            parameters.Add(("$kw", $"%{EscapeLike(query.Search)}%"));
        }

        if (query.Status is { } status)
        {
            conditions.Add("status = $status");
            parameters.Add(("$status", status.ToString()));
        }

        if (query.Grade is { } grade)
        {
            conditions.Add("grade = $grade");
            parameters.Add(("$grade", grade.ToString()));
        }

        if (query.OnlyMine || userContext.Current.Scope == DataScope.Owned)
        {
            conditions.Add("owner = $actor");
            parameters.Add(("$actor", userContext.Current.Actor));
        }

        var sql = $"SELECT {Columns} FROM crm_customer WHERE {string.Join(" AND ", conditions)} ORDER BY updated_utc DESC;";

        return await connection
            .QueryAsync(null, sql, Map, cancellationToken, parameters.ToArray())
            .ConfigureAwait(false);
    }

    public async Task<Customer?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmCustomerRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var customer = await connection.QuerySingleAsync(
            null,
            $"SELECT {Columns} FROM crm_customer WHERE id = $id AND is_deleted = 0;",
            Map,
            cancellationToken,
            ("$id", id)).ConfigureAwait(false);

        if (customer is null)
        {
            return null;
        }

        if (userContext.Current.Scope == DataScope.Owned && customer.Owner != userContext.Current.Actor)
        {
            return null;
        }

        return customer;
    }

    public async Task<string> SaveAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmCustomerWrite);

        return await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var existing = await connection.QuerySingleAsync(
                transaction,
                $"SELECT {Columns} FROM crm_customer WHERE id = $id;",
                Map,
                cancellationToken,
                ("$id", customer.Id)).ConfigureAwait(false);

            var now = clock.UtcNow;
            var record = customer with
            {
                Owner = string.IsNullOrWhiteSpace(customer.Owner) ? userContext.Current.Actor : customer.Owner,
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now
            };

            await connection.ExecuteAsync(
                transaction,
                """
                INSERT INTO crm_customer
                    (id, name, industry, scale, source, grade, status, owner, phone, email, website, address, tags, note, created_utc, updated_utc, is_deleted)
                VALUES
                    ($id, $name, $industry, $scale, $source, $grade, $status, $owner, $phone, $email, $website, $address, $tags, $note, $created, $updated, 0)
                ON CONFLICT(id) DO UPDATE SET
                    name = excluded.name,
                    industry = excluded.industry,
                    scale = excluded.scale,
                    source = excluded.source,
                    grade = excluded.grade,
                    status = excluded.status,
                    owner = excluded.owner,
                    phone = excluded.phone,
                    email = excluded.email,
                    website = excluded.website,
                    address = excluded.address,
                    tags = excluded.tags,
                    note = excluded.note,
                    updated_utc = excluded.updated_utc,
                    is_deleted = 0;
                """,
                cancellationToken,
                ("$id", record.Id),
                ("$name", record.Name),
                ("$industry", record.Industry),
                ("$scale", record.Scale),
                ("$source", record.Source),
                ("$grade", record.Grade.ToString()),
                ("$status", record.Status.ToString()),
                ("$owner", record.Owner),
                ("$phone", record.Phone),
                ("$email", record.Email),
                ("$website", record.Website),
                ("$address", record.Address),
                ("$tags", record.Tags),
                ("$note", record.Note),
                ("$created", SqliteValue.Db(record.CreatedUtc)),
                ("$updated", SqliteValue.Db(record.UpdatedUtc))).ConfigureAwait(false);

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = existing is null ? "crm.customer.create" : "crm.customer.update",
                    EntityType = "customer",
                    EntityId = record.Id,
                    Summary = record.Name,
                    BeforeJson = AuditJson.Of(existing),
                    AfterJson = AuditJson.Of(record)
                },
                cancellationToken).ConfigureAwait(false);

            return record.Id;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 软删除客户，并级联标记其联系人、报价：
    /// 跟进与日程保留，因为它们是"历史发生过的事实"，删客户不该抹掉工作痕迹。
    /// </summary>
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmCustomerDelete);

        await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var existing = await connection.QuerySingleAsync(
                transaction,
                $"SELECT {Columns} FROM crm_customer WHERE id = $id;",
                Map,
                cancellationToken,
                ("$id", id)).ConfigureAwait(false);

            if (existing is null)
            {
                return;
            }

            var now = SqliteValue.Db(clock.UtcNow);

            await connection.ExecuteAsync(
                transaction,
                "UPDATE crm_customer SET is_deleted = 1, updated_utc = $now WHERE id = $id;",
                cancellationToken,
                ("$id", id),
                ("$now", now)).ConfigureAwait(false);

            await connection.ExecuteAsync(
                transaction,
                "UPDATE crm_contact SET is_deleted = 1, updated_utc = $now WHERE customer_id = $id;",
                cancellationToken,
                ("$id", id),
                ("$now", now)).ConfigureAwait(false);

            await connection.ExecuteAsync(
                transaction,
                "UPDATE crm_quote SET is_deleted = 1, updated_utc = $now WHERE customer_id = $id;",
                cancellationToken,
                ("$id", id),
                ("$now", now)).ConfigureAwait(false);

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = "crm.customer.delete",
                    EntityType = "customer",
                    EntityId = id,
                    Summary = existing.Name,
                    BeforeJson = AuditJson.Of(existing)
                },
                cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CustomerStats> StatsAsync(CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmCustomerRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var monthStart = new DateTimeOffset(
            clock.TodayLocal.Year,
            clock.TodayLocal.Month,
            1,
            0, 0, 0, TimeSpan.Zero);

        var scopeFilter = userContext.Current.Scope == DataScope.Owned ? " AND owner = $actor" : string.Empty;
        var parameters = userContext.Current.Scope == DataScope.Owned
            ? new (string, object?)[] { ("$month", SqliteValue.Db(monthStart)), ("$actor", userContext.Current.Actor) }
            : [("$month", SqliteValue.Db(monthStart))];

        var total = await connection.ScalarLongAsync(
            null, $"SELECT COUNT(1) FROM crm_customer WHERE is_deleted = 0{scopeFilter};",
            cancellationToken, parameters).ConfigureAwait(false);

        var newThisMonth = await connection.ScalarLongAsync(
            null, $"SELECT COUNT(1) FROM crm_customer WHERE is_deleted = 0 AND created_utc >= $month{scopeFilter};",
            cancellationToken, parameters).ConfigureAwait(false);

        var wonThisMonth = await connection.ScalarLongAsync(
            null, $"SELECT COUNT(1) FROM crm_customer WHERE is_deleted = 0 AND status = 'Won' AND updated_utc >= $month{scopeFilter};",
            cancellationToken, parameters).ConfigureAwait(false);

        var pending = await connection.ScalarLongAsync(
            null,
            """
            SELECT COUNT(1) FROM crm_activity
            WHERE is_deleted = 0 AND next_follow_utc IS NOT NULL AND next_follow_utc <= $soon
            """,
            cancellationToken,
            [("$soon", SqliteValue.Db(clock.UtcNow.AddDays(3)))]).ConfigureAwait(false);

        return new CustomerStats((int)total, (int)newThisMonth, (int)pending, (int)wonThisMonth);
    }

    public static Customer Map(SqliteDataReader reader) => new()
    {
        Id = reader.Str(0),
        Name = reader.Str(1),
        Industry = reader.Str(2),
        Scale = reader.Str(3),
        Source = reader.Str(4),
        Grade = Enum.TryParse<CustomerGrade>(reader.Str(5), out var grade) ? grade : CustomerGrade.C,
        Status = Enum.TryParse<CustomerStatus>(reader.Str(6), out var status) ? status : CustomerStatus.Lead,
        Owner = reader.Str(7),
        Phone = reader.Str(8),
        Email = reader.Str(9),
        Website = reader.Str(10),
        Address = reader.Str(11),
        Tags = reader.Str(12),
        Note = reader.Str(13),
        CreatedUtc = reader.Dto(14),
        UpdatedUtc = reader.Dto(15)
    };

    /// <summary>把用户输入里的 LIKE 通配符转义掉，避免 "%" 变成全表匹配。</summary>
    public static string EscapeLike(string input) =>
        input.Replace("\\", "\\\\", StringComparison.Ordinal)
             .Replace("%", "\\%", StringComparison.Ordinal)
             .Replace("_", "\\_", StringComparison.Ordinal);
}