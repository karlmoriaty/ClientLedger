using System.Collections.ObjectModel;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Data;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;
using ClientLedger.Shell.Services;
using ClientLedger.Shell.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientLedger.Modules.Crm.ViewModels;

public sealed record QuoteStatusFilter(QuoteStatus? Value, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class QuotesViewModel : PageViewModel
{
    private readonly QuoteStore _quoteStore;
    private readonly CustomerStore _customerStore;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly IAppLog _log;

    private int _listVersion;

    public QuotesViewModel(
        QuoteStore quoteStore,
        CustomerStore customerStore,
        IPermissionService permissions,
        IDialogService dialogs,
        IAppLog log)
    {
        _quoteStore = quoteStore;
        _customerStore = customerStore;
        _permissions = permissions;
        _dialogs = dialogs;
        _log = log;

        StatusFilters =
        [
            new QuoteStatusFilter(null, "全部状态"),
            new QuoteStatusFilter(QuoteStatus.Draft, "草稿"),
            new QuoteStatusFilter(QuoteStatus.Sent, "已发出"),
            new QuoteStatusFilter(QuoteStatus.Accepted, "已接受"),
            new QuoteStatusFilter(QuoteStatus.Rejected, "已拒绝"),
            new QuoteStatusFilter(QuoteStatus.Expired, "已过期")
        ];

        SelectedStatusFilter = StatusFilters[0];
    }

    public override string Title => "报价";

    public override string Subtitle => "明细、折扣、税额与状态流转";

    public ObservableCollection<QuoteView> Items { get; } = [];

    public ObservableCollection<Customer> Customers { get; } = [];

    public IReadOnlyList<QuoteStatusFilter> StatusFilters { get; }

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private QuoteStatusFilter? _selectedStatusFilter;

    [ObservableProperty]
    private QuoteView? _selected;

    [ObservableProperty]
    private QuoteEditor? _editor;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _noticeText = string.Empty;

    [ObservableProperty]
    private string _summaryText = string.Empty;

    [ObservableProperty]
    private bool _canWrite;

    [ObservableProperty]
    private bool _canDelete;

    public override async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        CanWrite = _permissions.Has(Permissions.CrmQuoteWrite);
        CanDelete = _permissions.Has(Permissions.CrmQuoteDelete);

        await LoadCustomersAsync(cancellationToken).ConfigureAwait(true);
        await RefreshListAsync(cancellationToken).ConfigureAwait(true);

        if (Selected is null && Items.Count > 0)
        {
            Selected = Items[0];
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshListAsync(CancellationToken.None);

    [RelayCommand]
    private async Task NewQuoteAsync()
    {
        if (!CanWrite)
        {
            NoticeText = "当前角色无权新建报价。";
            return;
        }

        try
        {
            var number = await _quoteStore.NextNumberAsync().ConfigureAwait(true);

            Selected = null;
            Editor = new QuoteEditor
            {
                Number = number,
                CustomerId = Customers.FirstOrDefault()?.Id ?? string.Empty,
                ValidUntilText = DateInput.Day(DateTimeOffset.UtcNow.AddDays(30))
            };
            Editor.AddLine();
            IsEditing = true;
            NoticeText = string.Empty;
        }
        catch (Exception ex)
        {
            _log.Error("生成报价单号失败。", ex);
            NoticeText = "新建报价失败，详见日志。";
        }
    }

    [RelayCommand]
    private void EditQuote()
    {
        if (Selected is null || !CanWrite)
        {
            return;
        }

        Editor = QuoteEditor.From(Selected);
        IsEditing = true;
        NoticeText = string.Empty;
    }

    [RelayCommand]
    private void AddLine() => Editor?.AddLine();

    [RelayCommand]
    private void RemoveLine(QuoteLineEditor line) => Editor?.RemoveLine(line);

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Editor is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Editor.Title))
        {
            NoticeText = "报价标题不能为空。";
            return;
        }

        if (string.IsNullOrWhiteSpace(Editor.CustomerId))
        {
            NoticeText = "请选择报价对应的客户。";
            return;
        }

        if (Editor.Lines.Count == 0)
        {
            NoticeText = "至少需要一条报价明细。";
            return;
        }

        try
        {
            var wasNew = Editor.IsNew;
            var customerName = Customers.FirstOrDefault(c => c.Id == Editor.CustomerId)?.Name ?? string.Empty;
            var view = Editor.ToView(customerName);
            await _quoteStore.SaveAsync(view).ConfigureAwait(true);

            IsEditing = false;
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            Selected = Items.FirstOrDefault(item => item.Quote.Id == view.Quote.Id);
            NoticeText = wasNew ? "报价已创建。" : "报价已保存。";
        }
        catch (Exception ex)
        {
            _log.Error("保存报价失败。", ex);
            NoticeText = $"保存失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
        Editor = null;
        NoticeText = string.Empty;
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null || !CanDelete)
        {
            return;
        }

        if (!_dialogs.Confirm(
                "删除报价",
                $"确定删除报价 {Selected.Quote.Number} 吗？删除后历史不再展示，但审计记录会保留。",
                "删除报价",
                danger: true))
        {
            return;
        }

        try
        {
            await _quoteStore.DeleteAsync(Selected.Quote.Id).ConfigureAwait(true);
            Selected = null;
            IsEditing = false;
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            NoticeText = "报价已删除。";
        }
        catch (Exception ex)
        {
            _log.Error("删除报价失败。", ex);
            NoticeText = $"删除失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task MarkSentAsync()
    {
        if (Selected is null || !CanWrite)
        {
            return;
        }

        await ChangeStatusAsync(Selected, QuoteStatus.Sent).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task MarkAcceptedAsync()
    {
        if (Selected is null || !CanWrite)
        {
            return;
        }

        await ChangeStatusAsync(Selected, QuoteStatus.Accepted).ConfigureAwait(true);
    }

    partial void OnSearchChanged(string value) => _ = RefreshListAsync(CancellationToken.None);

    partial void OnSelectedStatusFilterChanged(QuoteStatusFilter? value) => _ = RefreshListAsync(CancellationToken.None);

    partial void OnSelectedChanged(QuoteView? value) => HasSelection = value is not null;

    private async Task ChangeStatusAsync(QuoteView view, QuoteStatus status)
    {
        try
        {
            var updated = new QuoteView(
                view.Quote with { Status = status },
                view.Lines,
                view.Totals);

            await _quoteStore.SaveAsync(updated).ConfigureAwait(true);
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            Selected = Items.FirstOrDefault(item => item.Quote.Id == view.Quote.Id);
            NoticeText = $"报价状态已更新为「{CrmLabels.Text(status)}」。";
        }
        catch (Exception ex)
        {
            _log.Error("更新报价状态失败。", ex);
            NoticeText = $"操作失败：{ex.Message}";
        }
    }

    private async Task LoadCustomersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var customers = await _customerStore.ListAsync(new CustomerQuery(), cancellationToken).ConfigureAwait(true);
            Customers.Clear();
            foreach (var customer in customers)
            {
                Customers.Add(customer);
            }
        }
        catch (Exception ex)
        {
            _log.Error("加载客户下拉失败。", ex);
        }
    }

    private async Task RefreshListAsync(CancellationToken cancellationToken)
    {
        var version = ++_listVersion;

        try
        {
            var items = await _quoteStore.ListAsync(
                new QuoteQuery
                {
                    Search = Search,
                    Status = SelectedStatusFilter?.Value
                },
                cancellationToken).ConfigureAwait(true);

            if (version != _listVersion)
            {
                return;
            }

            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            SummaryText = $"共 {Items.Count} 份 · 合计 {Formats.Money(Items.Sum(item => item.TotalAmount))}";

            if (Selected is not null && Items.All(item => item.Quote.Id != Selected.Quote.Id))
            {
                Selected = null;
            }
        }
        catch (Exception ex)
        {
            _log.Error("加载报价列表失败。", ex);
            NoticeText = $"加载失败：{ex.Message}";
        }
    }
}