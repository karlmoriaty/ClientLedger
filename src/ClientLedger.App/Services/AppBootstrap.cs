using System.Reflection;
using ClientLedger.Data;
using ClientLedger.Data.Audit;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Licensing;
using ClientLedger.Modules.Crm;
using ClientLedger.Security;
using ClientLedger.Shell.Services;
using ClientLedger.Shell.ViewModels;
using ClientLedger.Shell.Views;
using Microsoft.Extensions.DependencyInjection;

namespace ClientLedger.App.Services;

/// <summary>
/// 组合根。所有实例在这里显式构造并注册，顺序即依赖顺序：
/// 先建平台原语 → 再让模块登记自己的服务 → 最后建容器。
/// </summary>
public static class AppBootstrap
{
    public static (ServiceProvider Provider, IReadOnlyList<IModule> Modules) Build(AppPaths paths, IAppLog log)
    {
        var clock = SystemClock.Instance;
        var identity = new WindowsIdentityProvider();
        var connectionFactory = new SqliteConnectionFactory(paths.DatabaseFilePath, log);
        var settings = new SettingsStore(connectionFactory, clock);
        var unitOfWork = new SqliteUnitOfWork(connectionFactory);
        var auditStore = new AuditStore(connectionFactory, identity, clock);
        var eventBus = new EventBus(log);

        var userContext = new UserContext(settings, identity);
        var permissionService = new PermissionService(userContext);
        var trialStore = new TrialStore(paths, identity, clock, log);
        var licenseService = new LicenseService(paths, identity, trialStore, clock, log);

        var modules = new List<IModule>
        {
            new CrmModule()
        };

        var moduleContext = new ModuleContext(paths, clock, eventBus, auditStore, log);

        var services = new ServiceCollection();

        services.AddSingleton(paths);
        services.AddSingleton(log);
        services.AddSingleton<IClock>(clock);
        services.AddSingleton<IIdentityProvider>(identity);
        services.AddSingleton<IDbConnectionFactory>(connectionFactory);
        services.AddSingleton<ISettingsStore>(settings);
        services.AddSingleton<IUnitOfWork>(unitOfWork);
        services.AddSingleton<IEventBus>(eventBus);
        services.AddSingleton<IUserContext>(userContext);
        services.AddSingleton<IPermissionService>(permissionService);
        services.AddSingleton<IAuditWriter>(auditStore);
        services.AddSingleton<IAuditQuery>(auditStore);
        services.AddSingleton(trialStore);
        services.AddSingleton<ILicenseService>(licenseService);

        var appAssembly = Assembly.GetEntryAssembly() ?? typeof(AppBootstrap).Assembly;
        services.AddSingleton(new AppInfo(appAssembly));

        services.AddSingleton<IReadOnlyList<IModule>>(modules);
        services.AddSingleton<IMigrationSource, PlatformMigrationSource>();
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<AppBootstrapRunner>();

        services.AddSingleton<DataMaintenanceService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<INavigationRegistry, NavigationRegistry>();
        services.AddSingleton<IUpdateService, VelopackUpdateService>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ShellWindow>();
        services.AddTransient<SettingsViewModel>();

        foreach (var module in modules)
        {
            module.ConfigureServices(services, moduleContext);
        }

        var provider = services.BuildServiceProvider();
        return (provider, modules);
    }
}