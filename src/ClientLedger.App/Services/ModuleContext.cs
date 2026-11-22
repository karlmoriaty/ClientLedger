using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;

namespace ClientLedger.App.Services;

/// <summary>模块可见的宿主能力。窄接口，模块拿不到容器、拿不到别的模块。</summary>
public sealed class ModuleContext(
    AppPaths paths,
    IClock clock,
    IEventBus events,
    IAuditWriter audit,
    IAppLog log) : IModuleContext
{
    public string DataDirectory => paths.ModuleDataRoot;

    public IClock Clock => clock;

    public IEventBus Events => events;

    public IAuditWriter Audit => audit;

    public IAppLog Log => log;
}