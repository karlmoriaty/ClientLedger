using System.Data;
using System.Data.Common;
using System.Reflection;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;

namespace ClientLedger.Data.Audit;

public sealed record AuditRecord(
    long Id,
    DateTimeOffset AtUtc,
    string ModuleId,
    string Actor,
    string Machine,
    string Action,
    string EntityType,
    string EntityId,
    string? Summary)
{
    public string AtLocalText => Formats.Moment(AtUtc);
}

public interface IAuditQuery
{
    Task<IReadOnlyList<AuditRecord>> RecentAsync(
        int limit = 200,
        string? moduleId = null,
        CancellationToken cancellationToken = default);

    Task<long> CountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 审计写入与查询。表只追加：没有 Update、没有 Delete，也不向模块暴露写入口，
/// 模块只能通过 IAuditWriter 落账。
/// </summary>
public sealed class AuditStore(
    IDbConnectionFactory factory,
    IIdentityProvider identity,
    IClock clock) : IAuditWriter, IAuditQuery
{
    private const string InsertSql = """
        INSERT INTO platform_audit
            (at_utc, module_id, actor, machine, app_version, action, entity_type, entity_id, summary, before_json, after_json)
        VALUES
            ($at, $module, $actor, $machine, $version, $action, $entity, $entityId, $summary, $before, $after);
        """;

    private static readonly string AppVersion =
        (Assembly.GetEntryAssembly() ?? typeof(AuditStore).Assembly).GetName().Version?.ToString(3) ?? "0.0.0";

    public async Task WriteAsync(DbTransaction transaction, AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        await InsertAsync(transaction.Connection!, transaction, entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: false);

        try
        {
            await InsertAsync(connection, transaction, entry, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<IReadOnlyList<AuditRecord>> RecentAsync(
        int limit = 200,
        string? moduleId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);

        var sql = """
            SELECT id, at_utc, module_id, actor, machine, action, entity_type, entity_id, summary
            FROM platform_audit
            """;

        if (!string.IsNullOrWhiteSpace(moduleId))
        {
            sql += " WHERE module_id = $module";
        }

        sql += " ORDER BY id DESC LIMIT $limit;";

        var parameters = string.IsNullOrWhiteSpace(moduleId)
            ? new (string, object?)[] { ("$limit", Math.Clamp(limit, 1, 2000)) }
            : [("$module", moduleId), ("$limit", Math.Clamp(limit, 1, 2000))];

        return await connection.QueryAsync(
            null,
            sql,
            reader => new AuditRecord(
                reader.GetInt64(0),
                reader.Dto(1),
                reader.Str(2),
                reader.Str(3),
                reader.Str(4),
                reader.Str(5),
                reader.Str(6),
                reader.Str(7),
                reader.StrOrNull(8)),
            cancellationToken,
            parameters).ConfigureAwait(false);
    }

    public async Task<long> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await connection
            .ScalarLongAsync(null, "SELECT COUNT(1) FROM platform_audit;", cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task InsertAsync(
        DbConnection connection,
        DbTransaction transaction,
        AuditEntry entry,
        CancellationToken cancellationToken)
    {
        await connection.ExecuteAsync(
            transaction,
            InsertSql,
            cancellationToken,
            ("$at", SqliteValue.Db(clock.UtcNow)),
            ("$module", entry.ModuleId),
            ("$actor", identity.Actor),
            ("$machine", identity.MachineName),
            ("$version", AppVersion),
            ("$action", entry.Action),
            ("$entity", entry.EntityType),
            ("$entityId", entry.EntityId),
            ("$summary", entry.Summary),
            ("$before", entry.BeforeJson),
            ("$after", entry.AfterJson)).ConfigureAwait(false);
    }
}