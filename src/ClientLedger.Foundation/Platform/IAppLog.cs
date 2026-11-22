using System.Globalization;
using System.Text;

namespace ClientLedger.Foundation.Platform;

public interface IAppLog
{
    string LogDirectory { get; }

    void Info(string message);

    void Warn(string message, Exception? exception = null);

    void Error(string message, Exception? exception = null);
}

/// <summary>
/// 极简按天滚动文件日志。刻意不引第三方日志框架：
/// 买断制单机软件的日志只需要"出了事能拿到现场"，不需要结构化管道。
/// </summary>
public sealed class FileAppLog : IAppLog
{
    private const int RetentionDays = 14;

    private readonly Lock _gate = new();

    public FileAppLog(string logDirectory)
    {
        LogDirectory = logDirectory;
        try
        {
            Directory.CreateDirectory(logDirectory);
            Cleanup();
        }
        catch
        {
            // 日志目录不可写时不能影响主流程。
        }
    }

    public string LogDirectory { get; }

    public void Info(string message) => Write("INFO", message, null);

    public void Warn(string message, Exception? exception = null) => Write("WARN", message, exception);

    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string message, Exception? exception)
    {
        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append(" [").Append(level).Append("] ")
            .Append(message);

        if (exception is not null)
        {
            line.AppendLine().Append(exception);
        }

        var text = line.ToString();
        try
        {
            var path = Path.Combine(LogDirectory, $"clientledger-{DateTime.Now:yyyy-MM-dd}.log");
            lock (_gate)
            {
                File.AppendAllText(path, text + Environment.NewLine, Encoding.UTF8);
            }
        }
        catch
        {
            // 忽略：日志失败不得引发二次故障。
        }

        System.Diagnostics.Debug.WriteLine(text);
    }

    private void Cleanup()
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        foreach (var file in Directory.EnumerateFiles(LogDirectory, "clientledger-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // 单个文件删不掉不影响其它文件。
            }
        }
    }
}