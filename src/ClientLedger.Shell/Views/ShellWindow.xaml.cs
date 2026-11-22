using System.Windows;
using ClientLedger.Foundation.Platform;
using ClientLedger.Shell.Controls;
using ClientLedger.Shell.ViewModels;

namespace ClientLedger.Shell.Views;

public partial class ShellWindow : ChromeWindow
{
    private readonly ShellViewModel _viewModel;
    private readonly IAppLog _log;

    public ShellWindow(ShellViewModel viewModel, IAppLog log)
    {
        _viewModel = viewModel;
        _log = log;

        InitializeComponent();
        DataContext = viewModel;
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;

        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _log.Error("主窗口初始化失败。", ex);
        }
    }
}