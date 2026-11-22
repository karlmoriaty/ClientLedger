namespace ClientLedger.Foundation.Contracts;

/// <summary>
/// 示例/种子数据写入扩展点。首次启动由宿主统一编排，
/// 模块自己决定"首次应该放什么进去"，宿主不碰模块的表。
/// </summary>
public interface IModuleSeedContributor
{
    string ModuleId { get; }

    Task EnsureSeedAsync(CancellationToken cancellationToken = default);
}