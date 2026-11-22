using ClientLedger.Foundation.Platform;
using Microsoft.Data.Sqlite;

namespace ClientLedger.Data;

public interface IDbConnectionFactory
{
    string DatabasePath { get; }

    Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// SQLite 连接工厂。PRAGMA 中只有 journal_mode 是持久设置，
/// foreign_keys / busy_timeout 都是"每连接"生效，必须每次打开都设 —— 漏了 foreign_keys
/// 外键会静默失效，这是 SQLite 最常见的隐形数据事故来源。
/// </summary>
public sealed class SqliteConnectionFactory : IDbConnectionFactory
{
    private readonly string _connectionString;
    private readonly IAppLog? _log;

    public SqliteConnectionFactory(string databasePath, IAppLog? log = null)
    {
        DatabasePath = databasePath;
        _log = log;

        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default,
            Pooling = true,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();
    }

    public string DatabasePath { get; }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ApplyPragmasAsync(connection, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private async Task ApplyPragmasAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 5000;
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _log?.Error("设置 SQLite PRAGMA 失败。", ex);
            throw;
        }
    }
}