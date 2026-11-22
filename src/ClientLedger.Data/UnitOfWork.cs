using System.Data;
using System.Data.Common;

namespace ClientLedger.Data;

/// <summary>
/// 工作单元。业务写入与审计必须在同一个 SQLite 事务里完成，
/// 因此这里把连接与事务一并交给调用方，而不是让每次写入各开各连接。
/// </summary>
public interface IUnitOfWork
{
    Task<T> ExecuteAsync<T>(
        Func<DbConnection, DbTransaction, Task<T>> work,
        CancellationToken cancellationToken = default);

    Task ExecuteAsync(
        Func<DbConnection, DbTransaction, Task> work,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 写事务一律使用 IMMEDIATE（deferred: false）：
/// 先拿写锁再干活，避免"读后升级写"导致的 SQLITE_BUSY 抖动。
/// </summary>
public sealed class SqliteUnitOfWork(IDbConnectionFactory factory) : IUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(
        Func<DbConnection, DbTransaction, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = connection.BeginTransaction(IsolationLevel.Serializable, deferred: false);

        try
        {
            var result = await work(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // 回滚失败时保留原始异常，避免掩盖真正的失败原因。
            }

            throw;
        }
    }

    public Task ExecuteAsync(
        Func<DbConnection, DbTransaction, Task> work,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(async (connection, transaction) =>
        {
            await work(connection, transaction).ConfigureAwait(false);
            return true;
        }, cancellationToken);
}