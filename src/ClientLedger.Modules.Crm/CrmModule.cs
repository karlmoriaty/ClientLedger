using ClientLedger.Data;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Modules.Crm.Data;
using ClientLedger.Modules.Crm.ViewModels;
using ClientLedger.Security;
using Microsoft.Extensions.DependencyInjection;

namespace ClientLedger.Modules.Crm;

/// <summary>
/// CRM 模块入口。新增一个垂直业务模块时，只需要复制这个形状：
/// 一个类库 + 一个 IModule + 自己的表和迁移，宿主不需要改一行代码。
/// </summary>
public sealed class CrmModule : IModule
{
    public ModuleManifest Manifest { get; } = new()
    {
        Id = "crm",
        DisplayName = "客户管理",
        Version = new Version(0, 1, 0),
        MinimumHostVersion = new Version(0, 1, 0),
        Description = "客户档案、联系人、跟进记录、日程、报价与基础报表",
        DeclaredPermissions = Permissions.All.Where(p => p.StartsWith("crm.", StringComparison.Ordinal)).ToList()
    };

    public IReadOnlyList<Uri> ResourceDictionaryUris { get; } =
    [
        new Uri("pack://application:,,,/ClientLedger.Modules.Crm;component/Themes/ModuleTemplates.xaml")
    ];

    public void ConfigureServices(IServiceCollection services, IModuleContext context)
    {
        services.AddSingleton<IMigrationSource, CrmMigrationSource>();

        services.AddSingleton<CustomerStore>();
        services.AddSingleton<ContactStore>();
        services.AddSingleton<ActivityStore>();
        services.AddSingleton<ScheduleStore>();
        services.AddSingleton<QuoteStore>();
        services.AddSingleton<ReportStore>();

        services.AddSingleton<CrmSeeder>();
        services.AddSingleton<IDemoDataContributor>(sp => sp.GetRequiredService<CrmSeeder>());
        services.AddSingleton<IModuleSeedContributor>(sp => sp.GetRequiredService<CrmSeeder>());

        services.AddTransient<DashboardViewModel>();
        services.AddTransient<CustomersViewModel>();
        services.AddTransient<ContactsViewModel>();
        services.AddTransient<ActivitiesViewModel>();
        services.AddTransient<ScheduleViewModel>();
        services.AddTransient<QuotesViewModel>();
        services.AddTransient<ReportsViewModel>();
    }

    public void RegisterNavigation(INavigationRegistry registry)
    {
        registry.Add(new NavigationItem
        {
            Id = "crm.dashboard",
            Title = "工作台",
            IconKey = "Grid",
            Order = 10,
            Permission = Permissions.CrmCustomerRead,
            ViewModelFactory = services => services.GetRequiredService<DashboardViewModel>()
        });

        registry.Add(new NavigationItem
        {
            Id = "crm.customers",
            Title = "客户",
            IconKey = "Building",
            Order = 20,
            Permission = Permissions.CrmCustomerRead,
            ViewModelFactory = services => services.GetRequiredService<CustomersViewModel>()
        });

        registry.Add(new NavigationItem
        {
            Id = "crm.contacts",
            Title = "联系人",
            IconKey = "Users",
            Order = 30,
            Permission = Permissions.CrmContactRead,
            ViewModelFactory = services => services.GetRequiredService<ContactsViewModel>()
        });

        registry.Add(new NavigationItem
        {
            Id = "crm.activities",
            Title = "跟进",
            IconKey = "History",
            Order = 40,
            Permission = Permissions.CrmActivityRead,
            ViewModelFactory = services => services.GetRequiredService<ActivitiesViewModel>()
        });

        registry.Add(new NavigationItem
        {
            Id = "crm.schedule",
            Title = "日程",
            IconKey = "Calendar",
            Order = 50,
            Permission = Permissions.CrmScheduleRead,
            ViewModelFactory = services => services.GetRequiredService<ScheduleViewModel>()
        });

        registry.Add(new NavigationItem
        {
            Id = "crm.quotes",
            Title = "报价",
            IconKey = "FileText",
            Order = 60,
            Permission = Permissions.CrmQuoteRead,
            ViewModelFactory = services => services.GetRequiredService<QuotesViewModel>()
        });

        registry.Add(new NavigationItem
        {
            Id = "crm.reports",
            Title = "报表",
            IconKey = "Chart",
            Order = 70,
            Permission = Permissions.CrmReportRead,
            ViewModelFactory = services => services.GetRequiredService<ReportsViewModel>()
        });
    }
}