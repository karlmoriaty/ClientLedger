using ClientLedger.Data;
using ClientLedger.Data.Audit;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;
using Microsoft.Data.Sqlite;

namespace ClientLedger.Modules.Crm.Data;

public sealed record ActivityQuery
{
    public string? CustomerId { get; init; }

    public ActivityKind? Kind { get; init; }

    public string? Search { get; init; }

    public int Limit { get; init; } = 200;
}

/// <summary>
/// 跟进记录。这是 CRM 里最容易被低估的表：它既是"发生了什么"的历史，
/// 也是"下一步做什么"的待办源，所以 next_follow_utc 必须参与首页统计。
/// </summary>
public sealed class ActivityStore(
    IUnitOfWork unitOfWork,
    IDbConnectionFactory factory,
    IPermissionService permissions,
    IUserContext userContext,
    IAuditWriter audit,
    IClock clock)
{
    private const string SelectSql = """
        SELECT a.id, a.customer_id, c.name, a.contact_id, COALESCE(ct.name, ''),
               a.kind, a.occurred_utc, a.summary, a.next_step, a.next_follow_utc,
               a.owner, a.created_utc, a.updated_utc
        FROM crm_activity a
        JOIN crm_customer c ON c.id = a.customer_id
        LEFT JOIN crm_contact ct ON ct.id = a.contact_id
        """;

    public async Task<IReadOnlyList<Activity>> ListAsync(
        ActivityQuery query,
        CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmActivityRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var conditions = new List<string> { "a.is_deleted = 0", "c.is_deleted = 0" };
        var parameters = new List<(string, object?)>();

        if (!string.IsNullOrWhiteSpace(query.CustomerId))
        {
            conditions.Add("a.customer_id = $customer");
            parameters.Add(("$customer", query.CustomerId));
        }

        if (query.Kind is { } kind)
        {
            conditions.Add("a.kind = $kind");
            parameters.Add(("$kind", kind.ToString()));
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            conditions.Add("(a.summary LIKE $kw ESCAPE '\\' OR c.name LIKE $kw ESCAPE '\\' OR a.next_step LIKE $kw ESCAPE '\\')");
            parameters.Add(("$kw", $"%{CustomerStore.EscapeLike(query.Search)}%"));
        }

        if (userContext.Current.Scope == DataScope.Owned)
        {
            conditions.Add("c.owner = $actor");
            parameters.Add(("$actor", userContext.Current.Actor));
        }

        var sql = $"""
            {SelectSql}
            WHERE {string.Join(" AND ", conditions)}
            ORDER BY a.occurred_utc DESC
            LIMIT $limit;
            """;

        parameters.Add(("$limit", Math.Clamp(query.Limit, 1, 2000)));

        return await connection
            .QueryAsync(null, sql, Map, cancellationToken, parameters.ToArray())
            .ConfigureAwait(false);
    }

    public async Task<Activity?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmActivityRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleAsync(
            null,
            $"{SelectSql} WHERE a.id = $id;",
            Map,
            cancellationToken,
            ("$id", id)).ConfigureAwait(false);
    }

    public async Task<string> SaveAsync(Activity activity, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmActivityWrite);

        return await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var existing = await connection.QuerySingleAsync(
                transaction,
                $"{SelectSql} WHERE a.id = $id;",
                Map,
                cancellationToken,
                ("$id", activity.Id)).ConfigureAwait(false);

            var now = clock.UtcNow;
            var record = activity with
            {
                Owner = string.IsNullOrWhiteSpace(activity.Owner) ? userContext.Current.Actor : activity.Owner,
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now
            };

            await connection.ExecuteAsync(
                transaction,
                """
                INSERT INTO crm_activity
                    (id, customer_id, contact_id, kind, occurred_utc, summary, next_step, next_follow_utc, owner, created_utc, updated_utc, is_deleted)
                VALUES
                    ($id, $customer, $contact, $kind, $occurred, $summary, $next, $follow, $owner, $created, $updated, 0)
                ON CONFLICT(id) DO UPDATE SET
                    customer_id = excluded.customer_id,
                    contact_id = excluded.contact_id,
                    kind = excluded.kind,
                    occurred_utc = excluded.occurred_utc,
                    summary = excluded.summary,
                    next_step = excluded.next_step,
                    next_follow_utc = excluded.next_follow_utc,
                    owner = excluded.owner,
                    updated_utc = excluded.updated_utc,
                    is_deleted = 0;
                """,
                cancellationToken,
                ("$id", record.Id),
                ("$customer", record.CustomerId),
                ("$contact", record.ContactId),
                ("$kind", record.Kind.ToString()),
                ("$occurred", SqliteValue.Db(record.OccurredUtc)),
                ("$summary", record.Summary),
                ("$next", record.NextStep),
                ("$follow", SqliteValue.Db(record.NextFollowUtc)),
                ("$owner", record.Owner),
                ("$created", SqliteValue.Db(record.CreatedUtc)),
                ("$updated", SqliteValue.Db(record.UpdatedUtc))).ConfigureAwait(false);

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = existing is null ? "crm.activity.create" : "crm.activity.update",
                    EntityType = "activity",
                    EntityId = record.Id,
                    Summary = $"{record.KindText} · {record.CustomerName}",
                    BeforeJson = AuditJson.Of(existing),
                    AfterJson = AuditJson.Of(record)
                },
                cancellationToken).ConfigureAwait(false);

            return record.Id;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmActivityWrite);

        await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var existing = await connection.QuerySingleAsync(
                transaction,
                $"{SelectSql} WHERE a.id = $id;",
                Map,
                cancellationToken,
                ("$id", id)).ConfigureAwait(false);

            if (existing is null)
            {
                return;
            }

            await connection.ExecuteAsync(
                transaction,
                "UPDATE crm_activity SET is_deleted = 1, updated_utc = $now WHERE id = $id;",
                cancellationToken,
                ("$id", id),
                ("$now", SqliteValue.Db(clock.UtcNow))).ConfigureAwait(false);

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = "crm.activity.delete",
                    EntityType = "activity",
                    EntityId = id,
                    Summary = existing.Summary,
                    BeforeJson = AuditJson.Of(existing)
                },
                cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static Activity Map(SqliteDataReader reader) => new()
    {
        Id = reader.Str(0),
        CustomerId = reader.Str(1),
        CustomerName = reader.Str(2),
        ContactId = reader.StrOrNull(3),
        ContactName = reader.Str(4),
        Kind = Enum.TryParse<ActivityKind>(reader.Str(5), out var kind) ? kind : ActivityKind.Other,
        OccurredUtc = reader.Dto(6),
        Summary = reader.Str(7),
        NextStep = reader.Str(8),
        NextFollowUtc = reader.DtoOrNull(9),
        Owner = reader.Str(10),
        CreatedUtc = reader.Dto(11),
        UpdatedUtc = reader.Dto(12)
    };
}