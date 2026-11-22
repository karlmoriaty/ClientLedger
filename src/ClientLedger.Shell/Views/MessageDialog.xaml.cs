using System.Windows;
using ClientLedger.Shell.Controls;

namespace ClientLedger.Shell.Views;

public partial class MessageDialog : ChromeWindow
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public static bool Show(
        string heading,
        string body,
        string confirmText,
        string? cancelText,
        bool danger,
        Window? owner)
    {
        var dialog = new MessageDialog
        {
            HeadingText = { Text = heading },
            BodyText = { Text = body },
            ConfirmButton = { Content = confirmText },
            Owner = owner ?? Application.Current?.MainWindow
        };

        dialog.ConfirmButton.Style = (Style)dialog.FindResource(danger ? "DangerSolidButton" : "PrimaryButton");

        if (cancelText is null)
        {
            dialog.CancelButton.Visibility = Visibility.Collapsed;
        }
        else
        {
            dialog.CancelButton.Content = cancelText;
        }

        return dialog.ShowDialog() == true;
    }

    private void OnConfirm(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;
}