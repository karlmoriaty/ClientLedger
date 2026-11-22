using System.Collections.ObjectModel;
using System.Windows;
using ClientLedger.Data;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Licensing;
using ClientLedger.Security;
using ClientLedger.Shell.Services;
using ClientLedger.Shell.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace ClientLedger.Shell.ViewModels;

/// <summary>
/// 外壳 ViewModel。只负责三件事：可见导航、当前页面、许可与身份状态。
/// 业务数据一律由模块页面自己加载，Shell 不碰 CRM 的表。
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly INavigationRegistry _registry;
    private readonly IPermissionService _permissions;
    private readonly IUserContext _userContext;
    private readonly ILicenseService _license;
    private readonly IAppLog _log;
    private readonly AppInfo _appInfo;
    private readonly IUpdateService _updateService;
    private readonly ISettingsStore _settings;

    public ShellViewModel(
        IServiceProvider services,
        INavigationRegistry registry,
        IPermissionService permissions,
        IUserContext userContext,
        ILicenseService license,
        IUpdateService updateService,
        ISettingsStore settings,
        IAppLog log,
        AppInfo appInfo)
    {
        _services = services;
        _registry = registry;
        _permissions = permissions;
        _userContext = userContext;
        _license = license;
        _updateService = updateService;
        _settings = settings;
        _log = log;
        _appInfo = appInfo;

        _userContext.Changed += (_, _) => RebuildNavigation();
        _license.Changed += (_, _) => UpdateLicenseBadge();
        _updateService.Changed += (_, _) => UpdateBadge();
        UpdateBadge();

        VersionText = $"v{_appInfo.DisplayVersion}";
    }

    public ObservableCollection<NavigationEntry> Entries { get; } = [];

    [ObservableProperty]
    private NavigationEntry? _selectedEntry;

    [ObservableProperty]
    private object? _currentPage;

    [ObservableProperty]
    private string _pageTitle = string.Empty;

    [ObservableProperty]
    private string _pageSubtitle = string.Empty;

    [ObservableProperty]
    private string _licenseText = string.Empty;

    [ObservableProperty]
    private string _licenseTone = "neutral";

    [ObservableProperty]
    private string _licenseMessage = string.Empty;

    [ObservableProperty]
    private string _userName = string.Empty;

    [ObservableProperty]
    private string _userInitial = "?";

    [ObservableProperty]
    private string _roleText = string.Empty;

    [ObservableProperty]
    private string _versionText = string.Empty;

    [ObservableProperty]
    private bool _hasUpdate;

    [ObservableProperty]
    private string _updateBadgeText = string.Empty;

    public async Task InitializeAsync()
    {
        UpdateLicenseBadge();
        UpdateIdentity();
        RebuildNavigation();

        if (Entries.Count > 0)
        {
            await ActivateEntryAsync(Entries[0]).ConfigureAwait(true);
            SelectedEntry = Entries[0];
        }

        _ = AutoCheckForUpdatesAsync();
    }

    private void UpdateBadge()
    {
        var snapshot = _updateService.Current;
        HasUpdate = snapshot.State is UpdateState.Available or UpdateState.Downloading or UpdateState.ReadyToRestart;
        UpdateBadgeText = snapshot.State switch
        {
            UpdateState.Available => $"有新版本 {snapshot.AvailableVersion}",
            UpdateState.Downloading => "正在下载更新",
            UpdateState.ReadyToRestart => "更新已就绪",
            _ => string.Empty
        };
    }

    private async Task AutoCheckForUpdatesAsync()
    {
        try
        {
            if (!_updateService.Current.IsInstalled)
            {
                return;
            }

            var autoCheck = await _settings.GetAsync("update.autoCheck").ConfigureAwait(true);
            if (autoCheck == "0")
            {
                return;
            }

            // 延迟几秒，避免更新检查与首屏数据加载争抢带宽和 UI 时间。
            await Task.Delay(TimeSpan.FromSeconds(8)).ConfigureAwait(true);
            await _updateService.CheckAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Warn("启动时自动检查更新失败。", ex);
        }
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        try
        {
            SelectedEntry = null;
            var page = _services.GetRequiredService<SettingsViewModel>();
            CurrentPage = page;
            PageTitle = page.Title;
            PageSubtitle = page.Subtitle;
            await page.ActivateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error("打开设置页失败。", ex);
        }
    }

    [RelayCommand]
    private void OpenPalette()
    {
        var owner = Application.Current?.MainWindow;
        if (owner is null)
        {
            return;
        }

        CommandPaletteWindow.Show(owner, Entries, entry => SelectedEntry = entry);
    }

    [RelayCommand]
    private Task RefreshAsync() =>
        CurrentPage is PageViewModel page ? page.ActivateAsync() : Task.CompletedTask;

    partial void OnSelectedEntryChanged(NavigationEntry? value)
    {
        if (value is null)
        {
            return;
        }

        _ = ActivateEntryAsync(value);
    }

    private async Task ActivateEntryAsync(NavigationEntry entry)
    {
        try
        {
            if (entry.Resolve(_services) is not PageViewModel page)
            {
                return;
            }

            CurrentPage = page;
            PageTitle = page.Title;
            PageSubtitle = page.Subtitle;
            await page.ActivateAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _log.Error($"打开页面「{entry.Title}」失败。", ex);
        }
    }

    private void RebuildNavigation()
    {
        UpdateIdentity();

        var previousId = SelectedEntry?.Id;
        var wasOnSettings = SelectedEntry is null && CurrentPage is SettingsViewModel;

        Entries.Clear();

        foreach (var item in _registry.Items.Where(i => i.Group == "main").OrderBy(i => i.Order))
        {
            if (item.Permission is not null && !_permissions.Has(item.Permission))
            {
                continue;
            }

            Entries.Add(new NavigationEntry
            {
                Id = item.Id,
                Title = item.Title,
                Group = item.Group,
                Order = item.Order,
                Permission = item.Permission,
                Icon = NavigationEntry.ResolveIcon(item.IconKey),
                Factory = item.ViewModelFactory
            });
        }

        if (Entries.Count == 0)
        {
            return;
        }

        var restored = Entries.FirstOrDefault(e => e.Id == previousId);
        if (restored is not null)
        {
            // 角色变化后重新激活当前页：数据可见范围可能已经改变。
            SelectedEntry = restored;
            _ = ActivateEntryAsync(restored);
            return;
        }

        if (wasOnSettings)
        {
            return;
        }

        SelectedEntry = Entries[0];
    }

    private void UpdateLicenseBadge()
    {
        var snapshot = _license.Current;
        LicenseText = snapshot.StatusText;
        LicenseMessage = snapshot.Message;
        LicenseTone = snapshot.Status switch
        {
            LicenseStatus.Licensed => "success",
            LicenseStatus.TrialActive => "warning",
            LicenseStatus.LicensedUpdateExpired => "warning",
            _ => "danger"
        };
    }

    private void UpdateIdentity()
    {
        var session = _userContext.Current;
        UserName = Environment.UserName;
        UserInitial = string.IsNullOrEmpty(UserName) ? "?" : UserName[..1].ToUpperInvariant();
        RoleText = $"{session.RoleDisplayName} · {session.ScopeDisplayName}";
    }
}