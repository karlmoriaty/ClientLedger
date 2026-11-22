using System.Data.Common;

namespace ClientLedger.Foundation.Contracts;

/// <summary>
/// 审计条目。actor / 机器名 / 时间 / 版本由写入器补齐，调用方只描述"发生了什么"。
/// </summary>
public sealed record AuditEntry
{
    public required string ModuleId { get; init; }

    /// <summary>动作令牌，例如 crm.customer.delete。</summary>
    public required string Action { get; init; }

    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public string? Summary { get; init; }

    public string? BeforeJson { get; init; }

    public string? AfterJson { get; init; }
}

/// <summary>
/// 审计写入器。接收事务版本是为了让业务写入与审计落在同一个 SQLite 事务里：
/// 不允许出现"数据改了、账没记"的中间态。
/// </summary>
public interface IAuditWriter
{
    Task WriteAsync(DbTransaction transaction, AuditEntry entry, CancellationToken cancellationToken = default);

    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
}