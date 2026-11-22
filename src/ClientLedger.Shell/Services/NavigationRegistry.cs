using ClientLedger.Foundation.Contracts;

namespace ClientLedger.Shell.Services;

/// <summary>导航注册表。模块在启动时把入口登记进来，Shell 只读取。</summary>
public sealed class NavigationRegistry : INavigationRegistry
{
    private readonly List<NavigationItem> _items = [];

    public IReadOnlyList<NavigationItem> Items => _items;

    public void Add(NavigationItem item)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(item.Id);
        _items.Add(item);
    }
}