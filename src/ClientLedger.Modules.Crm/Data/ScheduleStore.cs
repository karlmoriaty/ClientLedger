using ClientLedger.Data;
using ClientLedger.Data.Audit;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;
using Microsoft.Data.Sqlite;

namespace ClientLedger.Modules.Crm.Data;

public sealed record ScheduleQuery
{
    public DateTimeOffset? FromUtc { get; init; }

    public DateTimeOffset? ToUtc { get; init; }

    public ScheduleState? State { get; init; }

    public bool IncludeDone { get; init; } = true;
}

/// <summary>
/// 日程与提醒。日程是"事实"，所以删除是物理删除（只留审计），
/// 避免"待办列表里永远躺着一条看不见的已删记录"。
/// </summary>
public sealed class ScheduleStore(
    IUnitOfWork unitOfWork,
    IDbConnectionFactory factory,
    IPermissionService permissions,
    IUserContext userContext,
    IAuditWriter audit,
    IClock clock)
{
    private const string SelectSql = """
        SELECT s.id, s.title, s.kind, s.customer_id, COALESCE(c.name, ''),
               s.start_utc, s.end_utc, s.remind_minutes, s.state, s.note,
               s.owner, s.created_utc, s.updated_utc
        FROM crm_schedule s
        LEFT JOIN crm_customer c ON c.id = s.customer_id
        """;

    public async Task<IReadOnlyList<ScheduleItem>> ListAsync(
        ScheduleQuery query,
        CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmScheduleRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var conditions = new List<string> { "1 = 1" };
        var parameters = new List<(string, object?)>();

        if (query.FromUtc is { } from)
        {
            conditions.Add("s.start_utc >= $from");
            parameters.Add(("$from", SqliteValue.Db(from)));
        }

        if (query.ToUtc is { } to)
        {
            conditions.Add("s.start_utc < $to");
            parameters.Add(("$to", SqliteValue.Db(to)));
        }

        if (query.State is { } state)
        {
            conditions.Add("s.state = $state");
            parameters.Add(("$state", state.ToString()));
        }
        else if (!query.IncludeDone)
        {
            conditions.Add("s.state = 'Pending'");
        }

        if (userContext.Current.Scope == DataScope.Owned)
        {
            conditions.Add("s.owner = $actor");
            parameters.Add(("$actor", userContext.Current.Actor));
        }

        var sql = $"{SelectSql} WHERE {string.Join(" AND ", conditions)} ORDER BY s.start_utc;";

        return await connection
            .QueryAsync(null, sql, Map, cancellationToken, parameters.ToArray())
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ScheduleItem>> UpcomingAsync(int days = 7, CancellationToken cancellationToken = default)
    {
        var from = clock.UtcNow.AddHours(-1);
        var to = clock.UtcNow.AddDays(days);

        return await ListAsync(
            new ScheduleQuery { FromUtc = from, ToUtc = to, State = ScheduleState.Pending },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ScheduleItem?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmScheduleRead);

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection.QuerySingleAsync(
            null,
            $"{SelectSql} WHERE s.id = $id;",
            Map,
            cancellationToken,
            ("$id", id)).ConfigureAwait(false);
    }

    public async Task<string> SaveAsync(ScheduleItem item, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmScheduleWrite);

        return await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var existing = await connection.QuerySingleAsync(
                transaction,
                $"{SelectSql} WHERE s.id = $id;",
                Map,
                cancellationToken,
                ("$id", item.Id)).ConfigureAwait(false);

            var now = clock.UtcNow;
            var record = item with
            {
                Owner = string.IsNullOrWhiteSpace(item.Owner) ? userContext.Current.Actor : item.Owner,
                CreatedUtc = existing?.CreatedUtc ?? now,
                UpdatedUtc = now
            };

            await connection.ExecuteAsync(
                transaction,
                """
                INSERT INTO crm_schedule
                    (id, title, kind, customer_id, start_utc, end_utc, remind_minutes, state, note, owner, created_utc, updated_utc)
                VALUES
                    ($id, $title, $kind, $customer, $start, $end, $remind, $state, $note, $owner, $created, $updated)
                ON CONFLICT(id) DO UPDATE SET
                    title = excluded.title,
                    kind = excluded.kind,
                    customer_id = excluded.customer_id,
                    start_utc = excluded.start_utc,
                    end_utc = excluded.end_utc,
                    remind_minutes = excluded.remind_minutes,
                    state = excluded.state,
                    note = excluded.note,
                    owner = excluded.owner,
                    updated_utc = excluded.updated_utc;
                """,
                cancellationToken,
                ("$id", record.Id),
                ("$title", record.Title),
                ("$kind", record.Kind.ToString()),
                ("$customer", record.CustomerId),
                ("$start", SqliteValue.Db(record.StartUtc)),
                ("$end", SqliteValue.Db(record.EndUtc)),
                ("$remind", record.RemindMinutes),
                ("$state", record.State.ToString()),
                ("$note", record.Note),
                ("$owner", record.Owner),
                ("$created", SqliteValue.Db(record.CreatedUtc)),
                ("$updated", SqliteValue.Db(record.UpdatedUtc))).ConfigureAwait(false);

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = existing is null ? "crm.schedule.create" : "crm.schedule.update",
                    EntityType = "schedule",
                    EntityId = record.Id,
                    Summary = $"{record.Title} · {record.StartMoment}",
                    BeforeJson = AuditJson.Of(existing),
                    AfterJson = AuditJson.Of(record)
                },
                cancellationToken).ConfigureAwait(false);

            return record.Id;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task SetStateAsync(string id, ScheduleState state, CancellationToken cancellationToken = default)
    {
        var existing = await GetAsync(id, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            return;
        }

        await SaveAsync(existing with { State = state }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.CrmScheduleWrite);

        await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var existing = await connection.QuerySingleAsync(
                transaction,
                $"{SelectSql} WHERE s.id = $id;",
                Map,
                cancellationToken,
                ("$id", id)).ConfigureAwait(false);

            if (existing is null)
            {
                return;
            }

            await connection.ExecuteAsync(
                transaction,
                "DELETE FROM crm_schedule WHERE id = $id;",
                cancellationToken,
                ("$id", id)).ConfigureAwait(false);

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = "crm.schedule.delete",
                    EntityType = "schedule",
                    EntityId = id,
                    Summary = existing.Title,
                    BeforeJson = AuditJson.Of(existing)
                },
                cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static ScheduleItem Map(SqliteDataReader reader) => new()
    {
        Id = reader.Str(0),
        Title = reader.Str(1),
        Kind = Enum.TryParse<ScheduleKind>(reader.Str(2), out var kind) ? kind : ScheduleKind.Task,
        CustomerId = reader.StrOrNull(3),
        CustomerName = reader.Str(4),
        StartUtc = reader.Dto(5),
        EndUtc = reader.DtoOrNull(6),
        RemindMinutes = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
        State = Enum.TryParse<ScheduleState>(reader.Str(8), out var state) ? state : ScheduleState.Pending,
        Note = reader.Str(9),
        Owner = reader.Str(10),
        CreatedUtc = reader.Dto(11),
        UpdatedUtc = reader.Dto(12)
    };
}