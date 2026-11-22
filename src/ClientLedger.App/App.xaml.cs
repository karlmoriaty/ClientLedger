using System.Windows;
using System.Windows.Threading;
using ClientLedger.App.Services;
using ClientLedger.Data;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Shell.Views;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Velopack;

namespace ClientLedger.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private ServiceProvider? _provider;
    private IAppLog? _log;

    public App()
    {
        // Velopack 的安装/更新钩子必须在任何界面初始化之前处理。
        VelopackApp.Build().Run();
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new SingleInstanceGuard();
        if (!_singleInstance.IsPrimary)
        {
            MessageBox.Show(
                "ClientLedger 已经在运行，请切换到已有的窗口。",
                "ClientLedger",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown();
            return;
        }

        var paths = new AppPaths();
        _log = new FileAppLog(paths.LogDirectory);
        _log.Info($"ClientLedger 启动，数据目录：{paths.DataDirectory}");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        try
        {
            var (provider, modules) = AppBootstrap.Build(paths, _log);
            _provider = provider;

            // 模块的导航入口必须在 ShellViewModel 首次解析之前登记。
            var registry = provider.GetRequiredService<INavigationRegistry>();
            foreach (var module in modules)
            {
                module.RegisterNavigation(registry);
            }

            MergeModuleResources(modules);

            var runner = provider.GetRequiredService<AppBootstrapRunner>();

            // 窗口尚未创建，这里用线程池执行初始化，避免 UI 线程同步上下文参与等待。
            Task.Run(() => runner.RunAsync()).GetAwaiter().GetResult();

            var window = provider.GetRequiredService<ShellWindow>();
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex)
        {
            _log.Error("启动失败。", ex);
            MessageBox.Show(
                $"ClientLedger 启动失败：{ex.Message}\n\n详细日志：{paths.LogDirectory}",
                "ClientLedger",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _log?.Info("ClientLedger 退出。");

        CheckpointDatabase();

        if (_provider is not null)
        {
            try
            {
                _provider.Dispose();
            }
            catch
            {
                // 容器释放失败不阻断退出。
            }
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void MergeModuleResources(IReadOnlyList<IModule> modules)
    {
        foreach (var module in modules)
        {
            foreach (var uri in module.ResourceDictionaryUris)
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
            }
        }
    }

    /// <summary>
    /// 退出时把 WAL 合并回主库。否则用户手动复制 clientledger.db 时会丢掉最近写入，
    /// 这是"数据看起来丢了"的经典来源，必须在退出路径上兜住。
    /// </summary>
    private void CheckpointDatabase()
    {
        try
        {
            if (_provider?.GetService(typeof(IDbConnectionFactory)) is not IDbConnectionFactory factory)
            {
                return;
            }

            using var connection = factory.OpenAsync().GetAwaiter().GetResult();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _log?.Warn("退出时合并 WAL 失败，数据仍在 -wal 文件中。", ex);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log?.Error("未处理的界面线程异常。", e.Exception);

        MessageBox.Show(
            $"发生未处理的错误：{e.Exception.Message}\n\n错误已记录到日志，程序将继续运行。",
            "ClientLedger",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _log?.Error("未处理的进程级异常。", e.ExceptionObject as Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.Error("未观察的后台任务异常。", e.Exception);
        e.SetObserved();
    }
}