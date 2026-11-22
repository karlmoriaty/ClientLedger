using ClientLedger.Foundation.Platform;

namespace ClientLedger.App.Services;

/// <summary>
/// 单实例互斥。WPF 程序不做单实例会让两个进程同时写同一个 SQLite 文件，
/// 轻则锁冲突，重则用户以为"数据丢了"（其实写到另一个进程的内存视图里）。
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _disposed;

    public SingleInstanceGuard()
    {
        _mutex = new Mutex(initiallyOwned: true, AppPaths.SingleInstanceMutexName, out var createdNew);
        IsPrimary = createdNew;
    }

    public bool IsPrimary { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            if (IsPrimary)
            {
                _mutex.ReleaseMutex();
            }

            _mutex.Dispose();
        }
        catch
        {
            // 互斥对象释放失败不影响退出流程。
        }
    }
}