using ClientLedger.Data;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Licensing;
using ClientLedger.Security;

namespace ClientLedger.App.Services;

/// <summary>
/// 启动初始化。顺序有讲究：
/// 1) 迁移（表必须先存在）；2) 用户身份与角色（权限决定后续能做什么）；
/// 3) 许可证（决定可用状态）；4) 模块种子（空库首启体验）。
/// </summary>
public sealed class AppBootstrapRunner(
    DatabaseInitializer databaseInitializer,
    IUserContext userContext,
    ILicenseService license,
    IEnumerable<IModuleSeedContributor> seedContributors,
    IAppLog log)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await databaseInitializer.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await userContext.LoadAsync(cancellationToken).ConfigureAwait(false);
        await license.RefreshAsync(cancellationToken).ConfigureAwait(false);

        foreach (var contributor in seedContributors)
        {
            try
            {
                await contributor.EnsureSeedAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 示例数据失败不能阻断启动。
                log.Warn($"模块 {contributor.ModuleId} 写入示例数据失败。", ex);
            }
        }

        log.Info($"启动初始化完成。许可证状态：{license.Current.StatusText}。");
    }
}