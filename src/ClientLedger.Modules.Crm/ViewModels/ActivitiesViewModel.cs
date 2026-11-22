using System.Collections.ObjectModel;
using System.ComponentModel;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Data;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;
using ClientLedger.Shell.Services;
using ClientLedger.Shell.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientLedger.Modules.Crm.ViewModels;

public sealed record KindFilter(ActivityKind? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// 跟进页。列表按发生时间倒序，编辑面板里"下一步"和"下次跟进时间"是必填意识字段：
/// CRM 的价值不在记录过去，而在提醒接下来做什么。
/// </summary>
public sealed partial class ActivitiesViewModel : PageViewModel
{
    private readonly ActivityStore _activityStore;
    private readonly CustomerStore _customerStore;
    private readonly ContactStore _contactStore;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly IAppLog _log;

    private int _listVersion;

    public ActivitiesViewModel(
        ActivityStore activityStore,
        CustomerStore customerStore,
        ContactStore contactStore,
        IPermissionService permissions,
        IDialogService dialogs,
        IAppLog log)
    {
        _activityStore = activityStore;
        _customerStore = customerStore;
        _contactStore = contactStore;
        _permissions = permissions;
        _dialogs = dialogs;
        _log = log;

        KindFilters =
        [
            new KindFilter(null, "全部类型"),
            new KindFilter(ActivityKind.Call, "电话"),
            new KindFilter(ActivityKind.Visit, "拜访"),
            new KindFilter(ActivityKind.Email, "邮件"),
            new KindFilter(ActivityKind.WeChat, "微信"),
            new KindFilter(ActivityKind.Meeting, "会谈")
        ];

        SelectedKindFilter = KindFilters[0];
    }

    public override string Title => "跟进";

    public override string Subtitle => "谁在什么时候谈了什么，下一步做什么";

    public ObservableCollection<Activity> Items { get; } = [];

    public ObservableCollection<Customer> Customers { get; } = [];

    public ObservableCollection<Contact> EditorContacts { get; } = [];

    public IReadOnlyList<KindFilter> KindFilters { get; }

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private KindFilter? _selectedKindFilter;

    [ObservableProperty]
    private Activity? _selected;

    [ObservableProperty]
    private ActivityEditor? _editor;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private bool _hasSelection;

    [ObservableProperty]
    private string _noticeText = string.Empty;

    [ObservableProperty]
    private bool _canWrite;

    public override async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        CanWrite = _permissions.Has(Permissions.CrmActivityWrite);

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
    private void NewActivity()
    {
        if (!CanWrite)
        {
            NoticeText = "当前角色无权记录跟进。";
            return;
        }

        Selected = null;
        Editor = new ActivityEditor
        {
            CustomerId = Customers.FirstOrDefault()?.Id ?? string.Empty,
            OccurredText = DateInput.Moment(DateTimeOffset.UtcNow),
            NextFollowText = DateInput.Moment(DateTimeOffset.UtcNow.AddDays(2))
        };
        IsEditing = true;
        NoticeText = string.Empty;
    }

    [RelayCommand]
    private void EditActivity()
    {
        if (Selected is null || !CanWrite)
        {
            return;
        }

        Editor = ActivityEditor.From(Selected);
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

        if (string.IsNullOrWhiteSpace(Editor.Summary))
        {
            NoticeText = "跟进摘要不能为空。";
            return;
        }

        if (string.IsNullOrWhiteSpace(Editor.CustomerId))
        {
            NoticeText = "请选择这条跟进所属的客户。";
            return;
        }

        try
        {
            var wasNew = Editor.IsNew;
            var record = Editor.ToRecord(string.Empty);
            await _activityStore.SaveAsync(record).ConfigureAwait(true);

            IsEditing = false;
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            Selected = Items.FirstOrDefault(item => item.Id == record.Id);
            NoticeText = wasNew ? "跟进已记录。" : "跟进已保存。";
        }
        catch (Exception ex)
        {
            _log.Error("保存跟进失败。", ex);
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
        if (Selected is null || !CanWrite)
        {
            return;
        }

        if (!_dialogs.Confirm("删除跟进", "确认删除这条跟进记录吗？", "删除", danger: true))
        {
            return;
        }

        try
        {
            await _activityStore.DeleteAsync(Selected.Id).ConfigureAwait(true);
            Selected = null;
            IsEditing = false;
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            NoticeText = "跟进已删除。";
        }
        catch (Exception ex)
        {
            _log.Error("删除跟进失败。", ex);
            NoticeText = $"删除失败：{ex.Message}";
        }
    }

    partial void OnSearchChanged(string value) => _ = RefreshListAsync(CancellationToken.None);

    partial void OnSelectedKindFilterChanged(KindFilter? value) => _ = RefreshListAsync(CancellationToken.None);

    partial void OnSelectedChanged(Activity? value) => HasSelection = value is not null;

    partial void OnEditorChanged(ActivityEditor? oldValue, ActivityEditor? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.PropertyChanged -= OnEditorPropertyChanged;
        }

        if (newValue is not null)
        {
            newValue.PropertyChanged += OnEditorPropertyChanged;
            _ = LoadEditorContactsAsync(newValue.CustomerId);
        }
    }

    private async void OnEditorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ActivityEditor.CustomerId) || sender is not ActivityEditor editor)
        {
            return;
        }

        await LoadEditorContactsAsync(editor.CustomerId).ConfigureAwait(true);
    }

    private async Task LoadEditorContactsAsync(string customerId)
    {
        EditorContacts.Clear();

        if (string.IsNullOrWhiteSpace(customerId))
        {
            return;
        }

        try
        {
            var contacts = await _contactStore.ListAsync(customerId).ConfigureAwait(true);
            foreach (var contact in contacts)
            {
                EditorContacts.Add(contact);
            }
        }
        catch (Exception ex)
        {
            _log.Warn("加载客户联系人失败。", ex);
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
            var items = await _activityStore.ListAsync(
                new ActivityQuery
                {
                    Search = Search,
                    Kind = SelectedKindFilter?.Value,
                    Limit = 300
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
            _log.Error("加载跟进列表失败。", ex);
            NoticeText = $"加载失败：{ex.Message}";
        }
    }
}