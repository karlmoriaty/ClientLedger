namespace ClientLedger.Shell.Services;

public interface IDialogService
{
    bool Confirm(string title, string message, string confirmText = "确定", bool danger = false);

    void Alert(string title, string message);

    string? PickOpenFile(string filter, string title = "选择文件");

    string? PickSaveFile(string filter, string suggestedName, string title = "保存文件");
}