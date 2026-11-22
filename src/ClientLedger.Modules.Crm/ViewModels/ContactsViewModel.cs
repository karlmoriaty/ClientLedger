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

public sealed partial class ContactsViewModel : PageViewModel
{
    private readonly ContactStore _contactStore;
    private readonly CustomerStore _customerStore;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly IAppLog _log;

    private int _listVersion;

    public ContactsViewModel(
        ContactStore contactStore,
        CustomerStore customerStore,
        IPermissionService permissions,
        IDialogService dialogs,
        IAppLog log)
    {
        _contactStore = contactStore;
        _customerStore = customerStore;
        _permissions = permissions;
        _dialogs = dialogs;
        _log = log;
    }

    public override string Title => "联系人";

    public override string Subtitle => "按客户归档的关键决策人";

    public ObservableCollection<Contact> Items { get; } = [];

    public ObservableCollection<Customer> Customers { get; } = [];

    [ObservableProperty]
    private string _search = string.Empty;

    [ObservableProperty]
    private Contact? _selected;

    [ObservableProperty]
    private ContactEditor? _editor;

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
        CanWrite = _permissions.Has(Permissions.CrmContactWrite);

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
    private void NewContact()
    {
        if (!CanWrite)
        {
            NoticeText = "当前角色无权新建联系人。";
            return;
        }

        Selected = null;
        Editor = new ContactEditor
        {
            CustomerId = Customers.FirstOrDefault()?.Id ?? string.Empty
        };
        IsEditing = true;
        NoticeText = string.Empty;
    }

    [RelayCommand]
    private void EditContact()
    {
        if (Selected is null || !CanWrite)
        {
            return;
        }

        Editor = ContactEditor.From(Selected);
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
            NoticeText = "联系人姓名不能为空。";
            return;
        }

        if (string.IsNullOrWhiteSpace(Editor.CustomerId))
        {
            NoticeText = "请选择联系人所属的客户。";
            return;
        }

        try
        {
            var wasNew = Editor.IsNew;
            var record = Editor.ToRecord();
            await _contactStore.SaveAsync(record).ConfigureAwait(true);

            IsEditing = false;
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            Selected = Items.FirstOrDefault(item => item.Id == record.Id);
            NoticeText = wasNew ? "联系人已创建。" : "联系人已保存。";
        }
        catch (Exception ex)
        {
            _log.Error("保存联系人失败。", ex);
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

        if (!_dialogs.Confirm(
                "删除联系人",
                $"确定删除「{Selected.Name}」吗？历史跟进记录会保留。",
                "删除联系人",
                danger: true))
        {
            return;
        }

        try
        {
            await _contactStore.DeleteAsync(Selected.Id).ConfigureAwait(true);
            Selected = null;
            IsEditing = false;
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            NoticeText = "联系人已删除。";
        }
        catch (Exception ex)
        {
            _log.Error("删除联系人失败。", ex);
            NoticeText = $"删除失败：{ex.Message}";
        }
    }

    partial void OnSearchChanged(string value) => _ = RefreshListAsync(CancellationToken.None);

    partial void OnSelectedChanged(Contact? value)
    {
        HasSelection = value is not null;

        if (value is not null && IsEditing)
        {
            return;
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
            var items = await _contactStore.ListAsync(search: Search, cancellationToken: cancellationToken)
                .ConfigureAwait(true);

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
            _log.Error("加载联系人列表失败。", ex);
            NoticeText = $"加载失败：{ex.Message}";
        }
    }
}