namespace ClientLedger.Foundation.Contracts;

/// <summary>
/// 示例数据清理扩展点。模块自己知道哪些记录是"示例"，宿主编排清理动作即可，
/// 不允许宿主直接写模块的表。
/// </summary>
public interface IDemoDataContributor
{
    string ModuleId { get; }

    /// <summary>删除本模块的示例数据，返回删除的记录数。</summary>
    Task<int> ClearDemoAsync(CancellationToken cancellationToken = default);
}