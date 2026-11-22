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

public sealed record StatusFilter(CustomerStatus? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// 客户页：左侧台账、右侧详情/编辑面板。
/// 面板内可快速记一条跟进，避免"为了写一句话跳三个页面"的典型 CRM 摩擦。
/// </summary>
public sealed partial class CustomersViewModel : PageViewModel
{
    private readonly CustomerStore _customerStore;
    private readonly ContactStore _contactStore;
    private readonly ActivityStore _activityStore;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly IClock _clock;
    private readonly IAppLog _log;

    private int _listVersion;
    private int _detailVersion;

    public CustomersViewModel(
        CustomerStore customerStore,
        ContactStore contactStore,
        ActivityStore activityStore,
        IPermissionService permissions,
        IDialogService dialogs,
        IClock clock,
        IAppLog log)
    {
        _customerStore = customerStore;
        _contactStore = contactStore;
        _activityStore = activityStore;
        _permissions = permissions;
        _dialogs = dialogs;
        _clock = clock;
        _log = log;

        StatusFilters =
        [
            new StatusFilter(null, "全部状态"),
            new StatusFilter(CustomerStatus.Lead, "线索"),
            new StatusFilter(CustomerStatus.Qualified, "已确认"),
            new StatusFilter(CustomerStatus.Negotiating, "洽谈中"),
            new StatusFilter(CustomerStatus.Won, "已成交"),
            new StatusFilter(CustomerStatus.Lost, "已流失")
        ];

        SelectedStatusFilter = StatusFilters[0];
    }

    public override string Title => "客户";

    public override string Subtitle => "档案、联系人与跟进时间线";

    public ObservableCollection<Customer> Items { get; } = [];

    public ObservableCollection<Contact> Contacts { get; } = [];

    public ObservableCollection<Activity> Timeline { get; } = [];

    public IReadOnlyList<StatusFilter> StatusFilters { get; }

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private StatusFilter? _selectedStatusFilter;

    [ObservableProperty]
    private Customer? _selected;

    [ObservableProperty]
    private CustomerEditor? _editor;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _noticeText = string.Empty;

    [ObservableProperty]
    private string _quickFollowUpText = string.Empty;

    [ObservableProperty]
    private bool _canWrite;

    [ObservableProperty]
    private bool _canDelete;

    [ObservableProperty]
    private bool _canFollowUp;

    public override async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        CanWrite = _permissions.Has(Permissions.CrmCustomerWrite);
        CanDelete = _permissions.Has(Permissions.CrmCustomerDelete);
        CanFollowUp = _permissions.Has(Permissions.CrmActivityWrite);

        await RefreshAsync(cancellationToken).ConfigureAwait(true);
    }

    [RelayCommand]
    private Task RefreshAsync() => RefreshAsync(CancellationToken.None);

    [RelayCommand]
    private void NewCustomer()
    {
        if (!CanWrite)
        {
            NoticeText = "当前角色无权新建客户。";
            return;
        }

        Selected = null;
        Editor = new CustomerEditor();
        IsEditing = true;
        NoticeText = string.Empty;
    }

    [RelayCommand]
    private void EditCustomer()
    {
        if (Selected is null || !CanWrite)
        {
            return;
        }

        Editor = CustomerEditor.From(Selected);
        IsEditing = true;
        NoticeText = string.Empty;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Editor is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(Editor.Name))
        {
            NoticeText = "客户名称不能为空。";
            return;
        }

        try
        {
            var wasNew = Editor.IsNew;
            var record = Editor.ToRecord(Selected?.Owner ?? string.Empty);
            await _customerStore.SaveAsync(record).ConfigureAwait(true);

            IsEditing = false;
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            Selected = Items.FirstOrDefault(item => item.Id == record.Id);
            NoticeText = wasNew ? "客户已创建。" : "客户已保存。";
        }
        catch (Exception ex)
        {
            _log.Error("保存客户失败。", ex);
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
                "删除客户",
                $"将删除「{Selected.Name}」及其联系人与报价记录。跟进与日程会作为历史保留。",
                "删除客户",
                danger: true))
        {
            return;
        }

        try
        {
            await _customerStore.DeleteAsync(Selected.Id).ConfigureAwait(true);
            Selected = null;
            IsEditing = false;
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            NoticeText = "客户已删除。";
        }
        catch (Exception ex)
        {
            _log.Error("删除客户失败。", ex);
            NoticeText = $"删除失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task QuickFollowUpAsync()
    {
        if (Selected is null || !CanFollowUp || string.IsNullOrWhiteSpace(QuickFollowUpText))
        {
            return;
        }

        try
        {
            var activity = new Activity
            {
                Id = Ids.New("act"),
                CustomerId = Selected.Id,
                Kind = ActivityKind.Call,
                OccurredUtc = _clock.UtcNow,
                Summary = QuickFollowUpText.Trim()
            };

            await _activityStore.SaveAsync(activity).ConfigureAwait(true);
            QuickFollowUpText = string.Empty;
            await LoadDetailAsync(Selected).ConfigureAwait(true);
            NoticeText = "已记录一条跟进。";
        }
        catch (Exception ex)
        {
            _log.Error("记录快速跟进失败。", ex);
            NoticeText = $"记录失败：{ex.Message}";
        }
    }

    partial void OnSearchChanged(string value) => _ = RefreshListAsync(CancellationToken.None);

    partial void OnSelectedStatusFilterChanged(StatusFilter? value) => _ = RefreshListAsync(CancellationToken.None);

    partial void OnSelectedChanged(Customer? value)
    {
        HasSelection = value is not null;

        if (value is null)
        {
            Contacts.Clear();
            Timeline.Clear();
            return;
        }

        if (IsEditing)
        {
            return;
        }

        _ = LoadDetailAsync(value);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await RefreshListAsync(cancellationToken).ConfigureAwait(true);

        if (Selected is null && Items.Count > 0)
        {
            Selected = Items[0];
        }
    }

    private async Task RefreshListAsync(CancellationToken cancellationToken)
    {
        var version = ++_listVersion;

        try
        {
            var items = await _customerStore.ListAsync(
                new CustomerQuery
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

            if (Selected is not null && Items.All(item => item.Id != Selected.Id))
            {
                Selected = null;
            }
        }
        catch (Exception ex)
        {
            _log.Error("加载客户列表失败。", ex);
            NoticeText = $"加载失败：{ex.Message}";
        }
    }

    private async Task LoadDetailAsync(Customer customer)
    {
        var version = ++_detailVersion;

        try
        {
            var contacts = await _contactStore.ListAsync(customer.Id).ConfigureAwait(true);
            var timeline = await _activityStore.ListAsync(
                new ActivityQuery { CustomerId = customer.Id, Limit = 50 }).ConfigureAwait(true);

            if (version != _detailVersion)
            {
                return;
            }

            Contacts.Clear();
            foreach (var contact in contacts)
            {
                Contacts.Add(contact);
            }

            Timeline.Clear();
            foreach (var activity in timeline)
            {
                Timeline.Add(activity);
            }
        }
        catch (Exception ex)
        {
            _log.Error("加载客户详情失败。", ex);
        }
    }
}