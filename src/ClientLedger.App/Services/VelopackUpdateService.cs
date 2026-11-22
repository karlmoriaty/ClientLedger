using System.Windows;
using ClientLedger.Foundation.Platform;
using ClientLedger.Shell.Services;
using Velopack;
using Velopack.Sources;

namespace ClientLedger.App.Services;

/// <summary>
/// 基于 GitHub Releases 的更新实现。
/// 只有通过 Velopack Setup 安装的副本才具备更新能力；便携版/开发运行会明确提示，而不是静默失败。
/// </summary>
public sealed class VelopackUpdateService : IUpdateService
{
    private const string RepositoryUrl = "https://github.com/karlmoriaty/ClientLedger";

    private readonly UpdateManager _manager;
    private readonly IAppLog _log;

    private UpdateInfo? _pending;

    public VelopackUpdateService(IAppLog log)
    {
        _log = log;
        _manager = new UpdateManager(new GithubSource(RepositoryUrl, null, prerelease: false));

        Current = new UpdateSnapshot
        {
            State = _manager.IsInstalled ? UpdateState.Idle : UpdateState.NotInstalled,
            StatusText = _manager.IsInstalled
                ? "尚未检查更新。"
                : "当前为便携版或开发运行，自动更新仅在安装版中可用。",
            IsInstalled = _manager.IsInstalled
        };
    }

    public UpdateSnapshot Current { get; private set; }

    public event EventHandler? Changed;

    public async Task CheckAsync(CancellationToken cancellationToken = default)
    {
        if (!_manager.IsInstalled)
        {
            Set(UpdateState.NotInstalled, "当前为便携版或开发运行，自动更新仅在安装版中可用。");
            return;
        }

        Set(UpdateState.Checking, "正在检查更新…");

        try
        {
            var info = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);

            if (info is null)
            {
                Set(UpdateState.UpToDate, "已是最新版本。");
                return;
            }

            _pending = info;
            Set(
                UpdateState.Available,
                $"发现新版本 {info.TargetFullRelease.Version}。",
                version: info.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex)
        {
            _log.Error("检查更新失败。", ex);
            Set(UpdateState.Failed, DescribeFailure(ex, "检查更新失败"));
        }
    }

    public async Task DownloadAsync(CancellationToken cancellationToken = default)
    {
        if (_pending is null)
        {
            return;
        }

        Set(UpdateState.Downloading, "正在下载更新…", progress: 0);

        try
        {
            await _manager
                .DownloadUpdatesAsync(_pending, progress => Set(
                    UpdateState.Downloading,
                    $"正在下载更新 {progress}%…",
                    progress))
                .ConfigureAwait(false);

            Set(
                UpdateState.ReadyToRestart,
                "更新已下载，重启后生效。",
                progress: 100,
                version: _pending.TargetFullRelease.Version.ToString());
        }
        catch (Exception ex)
        {
            _log.Error("下载更新失败。", ex);
            Set(UpdateState.Failed, DescribeFailure(ex, "下载更新失败"));
        }
    }

    public void ApplyAndRestart()
    {
        if (_pending is null)
        {
            return;
        }

        try
        {
            _manager.ApplyUpdatesAndRestart(_pending);
        }
        catch (Exception ex)
        {
            _log.Error("应用更新失败。", ex);
            Set(UpdateState.Failed, "应用更新失败，请到 Releases 页面手动下载。");
        }
    }

    /// <summary>
    /// 把底层异常翻译成用户能理解的提示。GitHub 未认证 API 有速率限制（60 次/小时/IP），
    /// 直接把 403 原文抛给用户既看不懂也会引发恐慌，详细原因只写日志。
    /// </summary>
    private static string DescribeFailure(Exception ex, string prefix)
    {
        var message = ex.Message;

        if (message.Contains("403", StringComparison.Ordinal)
            || message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
        {
            return $"{prefix}：GitHub 接口访问受限，请稍后重试，或到 Releases 页面手动下载。";
        }

        if (message.Contains("404", StringComparison.Ordinal))
        {
            return $"{prefix}：更新源暂不可用，请稍后重试。";
        }

        return $"{prefix}，请检查网络后重试，或到 Releases 页面手动下载。";
    }

    private void Set(UpdateState state, string text, int progress = 0, string? version = null)
    {
        void Apply()
        {
            Current = new UpdateSnapshot
            {
                State = state,
                StatusText = text,
                ProgressPercent = progress,
                AvailableVersion = version ?? Current.AvailableVersion,
                IsInstalled = _manager.IsInstalled
            };

            Changed?.Invoke(this, EventArgs.Empty);
        }

        var dispatcher = Application.Current?.Dispatcher;

        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Apply();
        }
        else
        {
            dispatcher.BeginInvoke(Apply);
        }
    }
}