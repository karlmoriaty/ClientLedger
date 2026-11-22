using ClientLedger.Shell.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Media;

namespace ClientLedger.Shell.ViewModels;

/// <summary>
/// 页面 ViewModel 基类。模块页面只描述标题、副标题和激活时的刷新逻辑，
/// 宿主负责实例化、缓存与切换。
/// </summary>
public abstract class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    public virtual string Subtitle => string.Empty;

    /// <summary>每次切换到本页时调用，用于刷新数据。</summary>
    public virtual Task ActivateAsync(CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}

/// <summary>导航项。Icon 直接从样式表解析，避免各模块自带图标资源。</summary>
public sealed class NavigationEntry
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Group { get; init; }

    public required int Order { get; init; }

    public string? Permission { get; init; }

    public Geometry? Icon { get; init; }

    public required Func<IServiceProvider, object> Factory { get; init; }

    private object? _instance;

    public object Resolve(IServiceProvider services)
    {
        _instance ??= Factory(services);
        return _instance;
    }

    public static Geometry? ResolveIcon(string iconKey) =>
        System.Windows.Application.Current?.TryFindResource($"Icon.{iconKey}") as Geometry;

    /// <summary>UI 自动化与日志都按这个读，不能落回类型名。</summary>
    public override string ToString() => Title;
}