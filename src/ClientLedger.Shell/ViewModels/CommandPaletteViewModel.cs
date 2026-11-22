using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ClientLedger.Shell.ViewModels;

public sealed class CommandPaletteItem
{
    public required string Title { get; init; }

    public string Hint { get; init; } = string.Empty;

    public System.Windows.Media.Geometry? Icon { get; init; }

    public NavigationEntry? Entry { get; init; }
}

/// <summary>命令面板。M0 支持页面跳转，后续可挂"新建客户""导入许可证"等动作。</summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly List<CommandPaletteItem> _all;

    public CommandPaletteViewModel(IReadOnlyList<NavigationEntry> entries)
    {
        _all = entries
            .Select(entry => new CommandPaletteItem
            {
                Title = entry.Title,
                Hint = "页面",
                Icon = entry.Icon,
                Entry = entry
            })
            .ToList();

        ApplyFilter();
    }

    public ObservableCollection<CommandPaletteItem> Items { get; } = [];

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private CommandPaletteItem? _selectedItem;

    partial void OnQueryChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = Query.Trim();

        Items.Clear();

        foreach (var item in _all)
        {
            if (query.Length == 0 || item.Title.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Items.Add(item);
            }
        }

        SelectedItem = Items.FirstOrDefault();
    }
}