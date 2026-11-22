namespace ClientLedger.Foundation.Platform;

/// <summary>
/// 所有落盘路径的唯一来源。安装目录是只读的，任何用户数据都不允许写在那里。
/// </summary>
public sealed class AppPaths
{
    public AppPaths(string? rootOverride = null, string? sharedOverride = null)
    {
        RootDirectory = rootOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClientLedger");

        SharedDirectory = sharedOverride
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClientLedger");

        DataDirectory = Path.Combine(RootDirectory, "data");
        LogDirectory = Path.Combine(RootDirectory, "logs");
        BackupDirectory = Path.Combine(RootDirectory, "backups");
        ModuleDataRoot = Path.Combine(RootDirectory, "modules");
        LocksDirectory = Path.Combine(RootDirectory, "locks");
        DatabaseFilePath = Path.Combine(DataDirectory, "clientledger.db");

        EnsureDirectory(RootDirectory);
        EnsureDirectory(DataDirectory);
        EnsureDirectory(LogDirectory);
        EnsureDirectory(BackupDirectory);
        EnsureDirectory(ModuleDataRoot);
        EnsureDirectory(LocksDirectory);
    }

    public string RootDirectory { get; }

    /// <summary>机器级共享目录，用于存放试用记录等"换 Windows 账户也要生效"的状态。</summary>
    public string SharedDirectory { get; }

    public string DataDirectory { get; }

    public string LogDirectory { get; }

    public string BackupDirectory { get; }

    public string ModuleDataRoot { get; }

    public string LocksDirectory { get; }

    public string DatabaseFilePath { get; }

    public string GetModuleDataDirectory(string moduleId)
    {
        var path = Path.Combine(ModuleDataRoot, moduleId);
        EnsureDirectory(path);
        return path;
    }

    /// <summary>单实例互斥用的命名对象名，按用户区分。</summary>
    public static string SingleInstanceMutexName =>
        $@"Local\ClientLedger.{Environment.UserName}.{Environment.MachineName}";

    private static void EnsureDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }
}