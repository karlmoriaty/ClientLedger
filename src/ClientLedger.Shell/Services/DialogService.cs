using System.Windows;
using ClientLedger.Shell.Views;
using Microsoft.Win32;

namespace ClientLedger.Shell.Services;

public sealed class DialogService : IDialogService
{
    public bool Confirm(string title, string message, string confirmText = "确定", bool danger = false) =>
        MessageDialog.Show(title, message, confirmText, "取消", danger, Owner);

    public void Alert(string title, string message) =>
        MessageDialog.Show(title, message, "知道了", cancelText: null, danger: false, Owner);

    public string? PickOpenFile(string filter, string title = "选择文件")
    {
        var dialog = new OpenFileDialog
        {
            Filter = filter,
            Title = title,
            CheckFileExists = true,
            Multiselect = false
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string filter, string suggestedName, string title = "保存文件")
    {
        var dialog = new SaveFileDialog
        {
            Filter = filter,
            Title = title,
            FileName = suggestedName,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(Owner) == true ? dialog.FileName : null;
    }

    private static Window? Owner => Application.Current?.MainWindow;
}