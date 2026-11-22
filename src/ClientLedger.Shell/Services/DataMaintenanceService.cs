using System.Diagnostics;
using System.IO;
using ClientLedger.Data;
using ClientLedger.Foundation.Platform;
using Microsoft.Data.Sqlite;

namespace ClientLedger.Shell.Services;

/// <summary>
/// 平台级数据维护。备份用 VACUUM INTO 而不是直接复制文件：
/// 在有活动连接、WAL 未合并时复制出来的数据库可能是坏的。
/// </summary>
public sealed class DataMaintenanceService(
    IDbConnectionFactory factory,
    AppPaths paths,
    IClock clock,
    IAppLog log)
{
    public async Task<string> BackupAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.BackupDirectory);

        var target = Path.Combine(
            paths.BackupDirectory,
            $"clientledger-{clock.UtcNow.ToLocalTime():yyyyMMdd-HHmmss}.db");

        await using var connection = (SqliteConnection)await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "VACUUM INTO $path;";
        command.Parameters.AddWithValue("$path", target);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        log.Info($"数据备份已生成：{target}");
        return target;
    }

    public void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = paths.DataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            log.Warn("打开数据目录失败。", ex);
        }
    }

    public void OpenLogFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = paths.LogDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            log.Warn("打开日志目录失败。", ex);
        }
    }
}