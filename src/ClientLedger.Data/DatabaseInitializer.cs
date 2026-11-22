using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using Microsoft.Data.Sqlite;

namespace ClientLedger.Data;

/// <summary>
/// 启动期数据库准备：建库、逐模块迁移、迁移前自动备份。
/// 迁移是"要么整体成功、要么整体回滚"的，绝不允许半个版本落库。
/// </summary>
public sealed class DatabaseInitializer(
    IDbConnectionFactory factory,
    IEnumerable<IMigrationSource> sources,
    IClock clock,
    IAppLog log)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = (SqliteConnection)await factory
            .OpenAsync(cancellationToken)
            .ConfigureAwait(false);

        await EnsureVersionTableAsync(connection, cancellationToken).ConfigureAwait(false);

        var pending = CollectPending(connection, out var anyPending);
        if (!anyPending)
        {
            return;
        }

        await BackupBeforeMigrationAsync(connection, cancellationToken).ConfigureAwait(false);

        foreach (var (moduleId, scripts) in pending)
        {
            foreach (var script in scripts)
            {
                await ApplyAsync(connection, script, cancellationToken).ConfigureAwait(false);
                log.Info($"已应用迁移：{moduleId} v{script.Version} ({script.Description})。");
            }
        }
    }

    private static async Task EnsureVersionTableAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS platform_schema_version (
                module_id   TEXT    NOT NULL,
                version     INTEGER NOT NULL,
                description TEXT    NOT NULL DEFAULT '',
                applied_utc TEXT    NOT NULL,
                PRIMARY KEY (module_id, version)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private Dictionary<string, List<MigrationScript>> CollectPending(
        SqliteConnection connection,
        out bool anyPending)
    {
        var applied = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);

        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT module_id, version FROM platform_schema_version;";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var moduleId = reader.GetString(0);
                if (!applied.TryGetValue(moduleId, out var versions))
                {
                    versions = [];
                    applied[moduleId] = versions;
                }

                versions.Add(reader.GetInt32(1));
            }
        }

        var pending = new Dictionary<string, List<MigrationScript>>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            foreach (var script in source.GetMigrations())
            {
                if (applied.TryGetValue(script.ModuleId, out var versions) && versions.Contains(script.Version))
                {
                    continue;
                }

                if (!pending.TryGetValue(script.ModuleId, out var list))
                {
                    list = [];
                    pending[script.ModuleId] = list;
                }

                list.Add(script);
            }
        }

        foreach (var list in pending.Values)
        {
            list.Sort((a, b) => a.Version.CompareTo(b.Version));
        }

        anyPending = pending.Count > 0;
        return pending;
    }

    private async Task BackupBeforeMigrationAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        try
        {
            var target = Path.Combine(
                Path.GetDirectoryName(factory.DatabasePath)!,
                "backups",
                $"pre-migration-{clock.UtcNow.ToLocalTime():yyyyMMdd-HHmmss}.db");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            await using var command = connection.CreateCommand();
            command.CommandText = "VACUUM INTO $path;";
            command.Parameters.AddWithValue("$path", target);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            log.Info($"迁移前备份已生成：{target}");
        }
        catch (Exception ex)
        {
            // 备份失败不阻断迁移，但必须留痕：宁可升级成功后再提示用户手动备份。
            log.Warn("迁移前备份失败，继续执行迁移。", ex);
        }
    }

    private async Task ApplyAsync(SqliteConnection connection, MigrationScript script, CancellationToken cancellationToken)
    {
        await using var transaction = connection.BeginTransaction(
            System.Data.IsolationLevel.Serializable,
            deferred: false);

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = script.Sql;
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await using (var stamp = connection.CreateCommand())
            {
                stamp.Transaction = transaction;
                stamp.CommandText = """
                    INSERT INTO platform_schema_version (module_id, version, description, applied_utc)
                    VALUES ($module, $version, $description, $applied);
                    """;
                stamp.Parameters.AddWithValue("$module", script.ModuleId);
                stamp.Parameters.AddWithValue("$version", script.Version);
                stamp.Parameters.AddWithValue("$description", script.Description);
                stamp.Parameters.AddWithValue("$applied", SqliteValue.Db(clock.UtcNow));
                await stamp.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}