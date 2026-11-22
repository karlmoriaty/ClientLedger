using Microsoft.Extensions.DependencyInjection;

namespace ClientLedger.Foundation.Contracts;

/// <summary>
/// 一个可加载的业务模块。宿主只认这三个入口，模块内部结构对宿主不可见。
/// </summary>
public interface IModule
{
    ModuleManifest Manifest { get; }

    /// <summary>向容器注册本模块的服务。禁止注册其它模块的实现。</summary>
    void ConfigureServices(IServiceCollection services, IModuleContext context);

    /// <summary>向导航注册本模块的入口。</summary>
    void RegisterNavigation(INavigationRegistry registry);

    /// <summary>
    /// 本模块提供的 ResourceDictionary 的 pack URI（用于把 ViewModel 映射到 View）。
    /// 宿主按顺序合并，模块自行管理自己的模板。
    /// </summary>
    IReadOnlyList<Uri> ResourceDictionaryUris => [];
}