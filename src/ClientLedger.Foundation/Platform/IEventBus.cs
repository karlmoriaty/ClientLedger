namespace ClientLedger.Foundation.Platform;

/// <summary>
/// 进程内事件总线。只用于模块之间"通知一下"的松耦合场景，
/// 不承担一致性职责：需要一致性的写入请走数据库事务。
/// </summary>
public interface IEventBus
{
    void Publish<T>(T message) where T : class;

    IDisposable Subscribe<T>(Action<T> handler) where T : class;
}

public sealed class EventBus(IAppLog? log = null) : IEventBus
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, List<object>> _handlers = [];

    public void Publish<T>(T message) where T : class
    {
        ArgumentNullException.ThrowIfNull(message);

        object[] snapshot;
        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list) || list.Count == 0)
            {
                return;
            }

            snapshot = [.. list];
        }

        foreach (var entry in snapshot)
        {
            var handler = (Action<T>)entry;
            try
            {
                handler(message);
            }
            catch (Exception ex)
            {
                // 一个订阅者出错不能拖垮其它订阅者，更不能让 UI 线程崩掉。
                log?.Error($"事件订阅者处理 {typeof(T).Name} 时失败。", ex);
            }
        }
    }

    public IDisposable Subscribe<T>(Action<T> handler) where T : class
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_gate)
        {
            if (!_handlers.TryGetValue(typeof(T), out var list))
            {
                list = [];
                _handlers[typeof(T)] = list;
            }

            list.Add(handler);
        }

        return new Subscription(this, typeof(T), handler);
    }

    private void Remove(Type type, object handler)
    {
        lock (_gate)
        {
            if (_handlers.TryGetValue(type, out var list))
            {
                list.Remove(handler);
                if (list.Count == 0)
                {
                    _handlers.Remove(type);
                }
            }
        }
    }

    private sealed class Subscription(EventBus owner, Type type, object handler) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Remove(type, handler);
            }
        }
    }
}