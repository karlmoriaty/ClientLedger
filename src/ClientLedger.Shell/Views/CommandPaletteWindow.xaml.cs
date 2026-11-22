using System.Windows;
using System.Windows.Input;
using ClientLedger.Shell.Controls;
using ClientLedger.Shell.ViewModels;

namespace ClientLedger.Shell.Views;

public partial class CommandPaletteWindow : ChromeWindow
{
    private readonly CommandPaletteViewModel _viewModel;
    private readonly Action<NavigationEntry> _onChosen;

    private CommandPaletteWindow(IReadOnlyList<NavigationEntry> entries, Action<NavigationEntry> onChosen)
    {
        _onChosen = onChosen;
        _viewModel = new CommandPaletteViewModel(entries);

        InitializeComponent();
        DataContext = _viewModel;
        Loaded += (_, _) => QueryBox.Focus();
    }

    public static void Show(Window owner, IReadOnlyList<NavigationEntry> entries, Action<NavigationEntry> onChosen)
    {
        var window = new CommandPaletteWindow(entries, onChosen)
        {
            Owner = owner
        };

        window.ShowDialog();
    }

    private void OnQueryKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                MoveSelection(1);
                e.Handled = true;
                break;
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Enter:
                ActivateSelection();
                e.Handled = true;
                break;
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
        }
    }

    private void OnListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            ActivateSelection();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnItemActivated(object sender, MouseButtonEventArgs e) => ActivateSelection();

    private void MoveSelection(int delta)
    {
        if (ResultList.Items.Count == 0)
        {
            return;
        }

        var index = ResultList.SelectedIndex + delta;
        index = Math.Clamp(index, 0, ResultList.Items.Count - 1);
        ResultList.SelectedIndex = index;
        ResultList.ScrollIntoView(ResultList.SelectedItem);
    }

    private void ActivateSelection()
    {
        if (_viewModel.SelectedItem?.Entry is not { } entry)
        {
            return;
        }

        _onChosen(entry);
        Close();
    }
}