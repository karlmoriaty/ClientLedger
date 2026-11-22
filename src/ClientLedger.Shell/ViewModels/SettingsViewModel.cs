using System.Collections.ObjectModel;
using ClientLedger.Data;
using ClientLedger.Data.Audit;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Licensing;
using ClientLedger.Security;
using ClientLedger.Shell.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientLedger.Shell.ViewModels;

public sealed record RoleOption(UserRole Role, string Name, string Description)
{
    public override string ToString() => Name;
}

public sealed record PermissionItem(string Token, string Label);

/// <summary>
/// 设置页。把底座的四个能力做成可观察、可操作的界面：
/// 角色与数据范围、审计追溯、许可证、数据维护。
/// </summary>
public sealed partial class SettingsViewModel : PageViewModel
{
    private readonly IUserContext _userContext;
    private readonly IPermissionService _permissions;
    private readonly ILicenseService _license;
    private readonly IAuditQuery _auditQuery;
    private readonly IEnumerable<IDemoDataContributor> _demoContributors;
    private readonly DataMaintenanceService _maintenance;
    private readonly IDialogService _dialogs;
    private readonly AppInfo _appInfo;
    private readonly AppPaths _paths;
    private readonly IUpdateService _updateService;
    private readonly ISettingsStore _settings;
    private readonly IAppLog _log;

    private bool _suppressRoleChange;
    private bool _suppressAutoCheckWrite;

    public SettingsViewModel(
        IUserContext userContext,
        IPermissionService permissions,
        ILicenseService license,
        IAuditQuery auditQuery,
        IEnumerable<IDemoDataContributor> demoContributors,
        DataMaintenanceService maintenance,
        IDialogService dialogs,
        AppInfo appInfo,
        AppPaths paths,
        IUpdateService updateService,
        ISettingsStore settings,
        IAppLog log)
    {
        _userContext = userContext;
        _permissions = permissions;
        _license = license;
        _auditQuery = auditQuery;
        _demoContributors = demoContributors;
        _maintenance = maintenance;
        _dialogs = dialogs;
        _appInfo = appInfo;
        _paths = paths;
        _updateService = updateService;
        _settings = settings;
        _log = log;

        _updateService.Changed += (_, _) => RefreshUpdateState();

        Roles =
        [
            new RoleOption(UserRole.Owner, RoleCatalog.DisplayName(UserRole.Owner), RoleCatalog.Description(UserRole.Owner)),
            new RoleOption(UserRole.Manager, RoleCatalog.DisplayName(UserRole.Manager), RoleCatalog.Description(UserRole.Manager)),
            new RoleOption(UserRole.Staff, RoleCatalog.DisplayName(UserRole.Staff), RoleCatalog.Description(UserRole.Staff)),
            new RoleOption(UserRole.ReadOnly, RoleCatalog.DisplayName(UserRole.ReadOnly), RoleCatalog.Description(UserRole.ReadOnly))
        ];

        VersionText = $"{_appInfo.ProductName} v{_appInfo.DisplayVersion}";
        DatabasePath = _paths.DatabaseFilePath;
        LogPath = _paths.LogDirectory;
        BackupPath = _paths.BackupDirectory;
    }

    public override string Title => "设置";

    public override string Subtitle => "权限、审计、授权与数据";

    public ObservableCollection<RoleOption> Roles { get; }

    public ObservableCollection<PermissionItem> PermissionItems { get; } = [];

    public ObservableCollection<AuditRecord> Audits { get; } = [];

    [ObservableProperty]
    private RoleOption? _selectedRole;

    [ObservableProperty]
    private string _identityText = string.Empty;

    [ObservableProperty]
    private string _scopeText = string.Empty;

    [ObservableProperty]
    private string _licenseStatusText = string.Empty;

    [ObservableProperty]
    private string _licenseTone = "neutral";

    [ObservableProperty]
    private string _licenseMessage = string.Empty;

    [ObservableProperty]
    private string _licenseIdText = "—";

    [ObservableProperty]
    private string _buyerText = "—";

    [ObservableProperty]
    private string _updatesUntilText = "—";

    [ObservableProperty]
    private string _auditCountText = string.Empty;

    [ObservableProperty]
    private string _databasePath = string.Empty;

    [ObservableProperty]
    private string _logPath = string.Empty;

    [ObservableProperty]
    private string _backupPath = string.Empty;

    [ObservableProperty]
    private string _versionText = string.Empty;

    [ObservableProperty]
    private string _noticeText = string.Empty;

    [ObservableProperty]
    private bool _canReadAudit;

    [ObservableProperty]
    private bool _canManageSettings;

    [ObservableProperty]
    private bool _canResetData;

    [ObservableProperty]
    private bool _canManageLicense;

    [ObservableProperty]
    private string _updateStatusText = string.Empty;

    [ObservableProperty]
    private string _availableVersionText = "—";

    [ObservableProperty]
    private int _updateProgress;

    [ObservableProperty]
    private bool _isUpdateBusy;

    [ObservableProperty]
    private bool _hasUpdateAvailable;

    [ObservableProperty]
    private bool _updateReadyToRestart;

    [ObservableProperty]
    private bool _autoCheckUpdates = true;

    [ObservableProperty]
    private bool _autoUpdateSupported;

    public override Task ActivateAsync(CancellationToken cancellationToken = default) =>
        LoadAsync(cancellationToken);

    [RelayCommand]
    private async Task ApplyRoleAsync()
    {
        if (SelectedRole is null)
        {
            return;
        }

        if (!_permissions.Has(Permissions.PlatformSettingsWrite))
        {
            NoticeText = "当前角色无权调整权限。";
            return;
        }

        try
        {
            await _userContext.SwitchRoleAsync(SelectedRole.Role).ConfigureAwait(true);
            NoticeText = $"已切换为「{SelectedRole.Name}」，导航与数据范围已同步调整。";
            await LoadAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("切换角色失败。", ex);
            NoticeText = "切换角色失败，详见日志。";
        }
    }

    [RelayCommand]
    private async Task ImportLicenseAsync()
    {
        if (!_permissions.Has(Permissions.PlatformLicenseManage))
        {
            NoticeText = "当前角色无权管理许可证。";
            return;
        }

        var path = _dialogs.PickOpenFile("许可证文件 (*.json)|*.json|所有文件 (*.*)|*.*", "导入许可证");
        if (path is null)
        {
            return;
        }

        var imported = await _license.ImportAsync(path).ConfigureAwait(true);
        await LoadAsync().ConfigureAwait(true);

        if (imported)
        {
            _dialogs.Alert("许可证已导入", _license.Current.Message);
        }
        else
        {
            _dialogs.Alert("导入失败", _license.Current.Message);
        }
    }

    [RelayCommand]
    private async Task BackupAsync()
    {
        if (!_permissions.Has(Permissions.PlatformDataBackup))
        {
            NoticeText = "当前角色无权备份数据。";
            return;
        }

        try
        {
            var target = await _maintenance.BackupAsync().ConfigureAwait(true);
            _dialogs.Alert("备份完成", $"备份文件已生成：\n{target}");
        }
        catch (Exception ex)
        {
            _log.Error("备份失败。", ex);
            _dialogs.Alert("备份失败", ex.Message);
        }
    }

    [RelayCommand]
    private async Task ClearDemoAsync()
    {
        if (!_permissions.Has(Permissions.PlatformDataReset))
        {
            NoticeText = "当前角色无权清除数据。";
            return;
        }

        if (!_dialogs.Confirm(
                "清除示例数据",
                "只会删除带示例标记的客户、联系人、跟进、日程与报价，你自己录入的数据不会受影响。",
                "清除示例数据",
                danger: true))
        {
            return;
        }

        var total = 0;
        foreach (var contributor in _demoContributors)
        {
            try
            {
                total += await contributor.ClearDemoAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _log.Error($"模块 {contributor.ModuleId} 清理示例数据失败。", ex);
            }
        }

        NoticeText = total == 0 ? "没有需要清理的示例数据。" : $"已清理 {total} 个示例客户及其关联记录。";
        await LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenDataFolder() => _maintenance.OpenDataFolder();

    [RelayCommand]
    private void OpenLogFolder() => _maintenance.OpenLogFolder();

    [RelayCommand]
    private async Task RefreshAuditsAsync() => await LoadAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task CheckUpdatesAsync() => await _updateService.CheckAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task DownloadUpdatesAsync() => await _updateService.DownloadAsync().ConfigureAwait(true);

    [RelayCommand]
    private void RestartToUpdate() => _updateService.ApplyAndRestart();

    partial void OnSelectedRoleChanged(RoleOption? value)
    {
        if (_suppressRoleChange || value is null)
        {
            return;
        }
    }

    private async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var session = _userContext.Current;

        _suppressRoleChange = true;
        SelectedRole = Roles.FirstOrDefault(role => role.Role == session.Role);
        _suppressRoleChange = false;

        IdentityText = session.Actor;
        ScopeText = session.ScopeDisplayName;

        CanReadAudit = session.Has(Permissions.PlatformAuditRead);
        CanManageSettings = session.Has(Permissions.PlatformSettingsWrite);
        CanResetData = session.Has(Permissions.PlatformDataReset);
        CanManageLicense = session.Has(Permissions.PlatformLicenseManage);

        PermissionItems.Clear();
        foreach (var token in ClientLedger.Security.Permissions.All.Where(session.Has))
        {
            PermissionItems.Add(new PermissionItem(token, ClientLedger.Security.Permissions.Describe(token)));
        }

        var snapshot = _license.Current;
        LicenseStatusText = snapshot.StatusText;
        LicenseMessage = snapshot.Message;
        LicenseTone = snapshot.Status switch
        {
            LicenseStatus.Licensed => "success",
            LicenseStatus.TrialActive => "warning",
            LicenseStatus.LicensedUpdateExpired => "warning",
            _ => "danger"
        };
        LicenseIdText = string.IsNullOrWhiteSpace(snapshot.LicenseId) ? "—" : snapshot.LicenseId;
        BuyerText = string.IsNullOrWhiteSpace(snapshot.Buyer) ? "—" : snapshot.Buyer;
        UpdatesUntilText = snapshot.UpdatesUntil is null
            ? (snapshot.Status is LicenseStatus.Licensed ? "永久" : "—")
            : Formats.Day(snapshot.UpdatesUntil.Value);

        if (CanReadAudit)
        {
            try
            {
                Audits.Clear();
                foreach (var record in await _auditQuery.RecentAsync(200, null, cancellationToken).ConfigureAwait(true))
                {
                    Audits.Add(record);
                }

                AuditCountText = $"最近 {Audits.Count} 条";
            }
            catch (Exception ex)
            {
                _log.Error("读取审计日志失败。", ex);
                AuditCountText = "读取失败";
            }
        }
        else
        {
            Audits.Clear();
            AuditCountText = "当前角色无权查看审计";
        }

        _suppressAutoCheckWrite = true;
        var autoCheck = await _settings.GetAsync("update.autoCheck", cancellationToken).ConfigureAwait(true);
        AutoCheckUpdates = autoCheck != "0";
        _suppressAutoCheckWrite = false;

        RefreshUpdateState();
    }

    partial void OnAutoCheckUpdatesChanged(bool value)
    {
        if (_suppressAutoCheckWrite)
        {
            return;
        }

        _ = _settings.SetAsync("update.autoCheck", value ? "1" : "0");
    }

    private void RefreshUpdateState()
    {
        var snapshot = _updateService.Current;
        UpdateStatusText = snapshot.StatusText;
        UpdateProgress = snapshot.ProgressPercent;
        HasUpdateAvailable = snapshot.State is UpdateState.Available;
        UpdateReadyToRestart = snapshot.State is UpdateState.ReadyToRestart;
        IsUpdateBusy = snapshot.State is UpdateState.Checking or UpdateState.Downloading;
        AutoUpdateSupported = snapshot.IsInstalled;
        AvailableVersionText = snapshot.AvailableVersion is { } version ? $"v{version}" : "—";
    }
}