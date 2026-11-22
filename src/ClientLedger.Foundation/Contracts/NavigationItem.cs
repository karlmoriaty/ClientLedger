namespace ClientLedger.Foundation.Contracts;

/// <summary>
/// 导航项。ViewModelFactory 由模块提供，宿主只负责实例化并放进内容区。
/// </summary>
public sealed record NavigationItem
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    /// <summary>图标资源键，定义在 Shell 的 Icons.xaml 中。</summary>
    public required string IconKey { get; init; }

    public required Func<IServiceProvider, object> ViewModelFactory { get; init; }

    /// <summary>为空表示所有角色可见。</summary>
    public string? Permission { get; init; }

    /// <summary>导航组：main / system。决定在左栏的分隔位置。</summary>
    public string Group { get; init; } = "main";

    public int Order { get; init; }
}

public interface INavigationRegistry
{
    void Add(NavigationItem item);

    IReadOnlyList<NavigationItem> Items { get; }
}