namespace ClientLedger.Security;

/// <summary>
/// 权限令牌。命名规则：模块.实体.动作。令牌是契约的一部分，模块声明、角色授予、
/// 命令校验三处必须用同一份常量，禁止在业务代码里手写字符串。
/// </summary>
public static class Permissions
{
    // ----- 平台 -----
    public const string PlatformAuditRead = "platform.audit.read";
    public const string PlatformSettingsWrite = "platform.settings.write";
    public const string PlatformLicenseManage = "platform.license.manage";
    public const string PlatformDataBackup = "platform.data.backup";
    public const string PlatformDataReset = "platform.data.reset";

    // ----- CRM -----
    public const string CrmCustomerRead = "crm.customer.read";
    public const string CrmCustomerWrite = "crm.customer.write";
    public const string CrmCustomerDelete = "crm.customer.delete";

    public const string CrmContactRead = "crm.contact.read";
    public const string CrmContactWrite = "crm.contact.write";

    public const string CrmActivityRead = "crm.activity.read";
    public const string CrmActivityWrite = "crm.activity.write";

    public const string CrmScheduleRead = "crm.schedule.read";
    public const string CrmScheduleWrite = "crm.schedule.write";

    public const string CrmQuoteRead = "crm.quote.read";
    public const string CrmQuoteWrite = "crm.quote.write";
    public const string CrmQuoteDelete = "crm.quote.delete";

    public const string CrmReportRead = "crm.report.read";
    public const string CrmDataExport = "crm.data.export";

    public static IReadOnlyList<string> All { get; } =
    [
        PlatformAuditRead, PlatformSettingsWrite, PlatformLicenseManage, PlatformDataBackup, PlatformDataReset,
        CrmCustomerRead, CrmCustomerWrite, CrmCustomerDelete,
        CrmContactRead, CrmContactWrite,
        CrmActivityRead, CrmActivityWrite,
        CrmScheduleRead, CrmScheduleWrite,
        CrmQuoteRead, CrmQuoteWrite, CrmQuoteDelete,
        CrmReportRead, CrmDataExport
    ];

    /// <summary>权限令牌 → 界面文案。新增权限时必须在这里补一条，否则权限页会出现裸令牌。</summary>
    public static string Describe(string permission) => permission switch
    {
        PlatformAuditRead => "查看审计日志",
        PlatformSettingsWrite => "调整角色与权限",
        PlatformLicenseManage => "管理许可证",
        PlatformDataBackup => "备份数据",
        PlatformDataReset => "清除示例数据",

        CrmCustomerRead => "查看客户档案",
        CrmCustomerWrite => "新建与编辑客户",
        CrmCustomerDelete => "删除客户",
        CrmContactRead => "查看联系人",
        CrmContactWrite => "新建与编辑联系人",
        CrmActivityRead => "查看跟进记录",
        CrmActivityWrite => "新建与编辑跟进记录",
        CrmScheduleRead => "查看日程",
        CrmScheduleWrite => "新建与编辑日程",
        CrmQuoteRead => "查看报价",
        CrmQuoteWrite => "新建与编辑报价",
        CrmQuoteDelete => "删除报价",
        CrmReportRead => "查看报表",
        CrmDataExport => "导出数据",
        _ => permission
    };
}

public enum UserRole
{
    Owner = 0,
    Manager = 1,
    Staff = 2,
    ReadOnly = 3
}

/// <summary>数据可见范围。UI 层藏按钮不等于安全，仓储查询必须再按此过滤一次。</summary>
public enum DataScope
{
    All = 0,
    Owned = 1
}

public static class RoleCatalog
{
    private static readonly HashSet<string> ReadOnlyPermissions = new(StringComparer.Ordinal)
    {
        Permissions.CrmCustomerRead, Permissions.CrmContactRead, Permissions.CrmActivityRead,
        Permissions.CrmScheduleRead, Permissions.CrmQuoteRead, Permissions.CrmReportRead
    };

    private static readonly HashSet<string> StaffPermissions = new(ReadOnlyPermissions, StringComparer.Ordinal)
    {
        Permissions.CrmCustomerWrite, Permissions.CrmContactWrite, Permissions.CrmActivityWrite,
        Permissions.CrmScheduleWrite, Permissions.CrmQuoteWrite
    };

    private static readonly HashSet<string> ManagerPermissions = new(StaffPermissions, StringComparer.Ordinal)
    {
        Permissions.CrmCustomerDelete, Permissions.CrmQuoteDelete, Permissions.CrmDataExport,
        Permissions.PlatformAuditRead, Permissions.PlatformDataBackup
    };

    public static IReadOnlySet<string> PermissionsFor(UserRole role) => role switch
    {
        UserRole.Owner => new HashSet<string>(Permissions.All, StringComparer.Ordinal),
        UserRole.Manager => ManagerPermissions,
        UserRole.Staff => StaffPermissions,
        _ => ReadOnlyPermissions
    };

    /// <summary>一线员工只能看到自己负责的客户，这既是权限也是企业里真实的诉求。</summary>
    public static DataScope ScopeFor(UserRole role) => role switch
    {
        UserRole.Staff => DataScope.Owned,
        _ => DataScope.All
    };

    public static string DisplayName(UserRole role) => role switch
    {
        UserRole.Owner => "所有者",
        UserRole.Manager => "主管",
        UserRole.Staff => "业务员",
        _ => "只读"
    };

    public static string Description(UserRole role) => role switch
    {
        UserRole.Owner => "全部权限，含授权、数据重置与审计。",
        UserRole.Manager => "可管理客户与报价、导出数据、查看审计；不能改授权与重置数据。",
        UserRole.Staff => "可录入客户、跟进、日程与报价；只能看到自己负责的客户。",
        _ => "只读全部数据，不能修改任何内容。"
    };
}