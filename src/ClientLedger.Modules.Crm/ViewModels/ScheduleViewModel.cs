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

public sealed record ScheduleFilter(ScheduleState? State, string Label)
{
    public override string ToString() => Label;
}

public sealed partial class ScheduleViewModel : PageViewModel
{
    private readonly ScheduleStore _scheduleStore;
    private readonly CustomerStore _customerStore;
    private readonly IPermissionService _permissions;
    private readonly IDialogService _dialogs;
    private readonly IClock _clock;
    private readonly IAppLog _log;

    private int _listVersion;

    public ScheduleViewModel(
        ScheduleStore scheduleStore,
        CustomerStore customerStore,
        IPermissionService permissions,
        IDialogService dialogs,
        IClock clock,
        IAppLog log)
    {
        _scheduleStore = scheduleStore;
        _customerStore = customerStore;
        _permissions = permissions;
        _dialogs = dialogs;
        _clock = clock;
        _log = log;

        Filters =
        [
            new ScheduleFilter(ScheduleState.Pending, "待完成"),
            new ScheduleFilter(ScheduleState.Done, "已完成"),
            new ScheduleFilter(null, "全部")
        ];

        SelectedFilter = Filters[0];
    }

    public override string Title => "日程";

    public override string Subtitle => "待办、会议与提醒";

    public ObservableCollection<ScheduleItem> Items { get; } = [];

    public ObservableCollection<Customer> Customers { get; } = [];

    public IReadOnlyList<ScheduleFilter> Filters { get; }

    [ObservableProperty]
    private ScheduleFilter? _selectedFilter;

    [ObservableProperty]
    private ScheduleItem? _selected;

    [ObservableProperty]
    private ScheduleEditor? _editor;

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
        CanWrite = _permissions.Has(Permissions.CrmScheduleWrite);

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
    private void NewSchedule()
    {
        if (!CanWrite)
        {
            NoticeText = "当前角色无权新建日程。";
            return;
        }

        var start = RoundToHalfHour(DateTimeOffset.UtcNow.AddHours(1));

        Selected = null;
        Editor = new ScheduleEditor
        {
            StartText = DateInput.Moment(start),
            EndText = DateInput.Moment(start.AddMinutes(30))
        };
        IsEditing = true;
        NoticeText = string.Empty;
    }

    [RelayCommand]
    private void EditSchedule()
    {
        if (Selected is null || !CanWrite)
        {
            return;
        }

        Editor = ScheduleEditor.From(Selected);
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

        if (string.IsNullOrWhiteSpace(Editor.Title))
        {
            NoticeText = "日程标题不能为空。";
            return;
        }

        var start = DateInput.ParseMomentOrNull(Editor.StartText);
        if (start is null)
        {
            NoticeText = "开始时间格式应为 2026-11-24 14:30。";
            return;
        }

        try
        {
            var wasNew = Editor.IsNew;
            var record = Editor.ToRecord(string.Empty);
            await _scheduleStore.SaveAsync(record).ConfigureAwait(true);

            IsEditing = false;
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            Selected = Items.FirstOrDefault(item => item.Id == record.Id);
            NoticeText = wasNew ? "日程已创建。" : "日程已保存。";
        }
        catch (Exception ex)
        {
            _log.Error("保存日程失败。", ex);
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
    private async Task CompleteAsync(ScheduleItem item)
    {
        if (!CanWrite)
        {
            return;
        }

        try
        {
            await _scheduleStore.SetStateAsync(item.Id, ScheduleState.Done).ConfigureAwait(true);
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            NoticeText = "日程已完成。";
        }
        catch (Exception ex)
        {
            _log.Error("完成日程失败。", ex);
            NoticeText = $"操作失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected is null || !CanWrite)
        {
            return;
        }

        if (!_dialogs.Confirm("删除日程", $"确定删除「{Selected.Title}」吗？", "删除", danger: true))
        {
            return;
        }

        try
        {
            await _scheduleStore.DeleteAsync(Selected.Id).ConfigureAwait(true);
            Selected = null;
            IsEditing = false;
            await RefreshListAsync(CancellationToken.None).ConfigureAwait(true);
            NoticeText = "日程已删除。";
        }
        catch (Exception ex)
        {
            _log.Error("删除日程失败。", ex);
            NoticeText = $"删除失败：{ex.Message}";
        }
    }

    partial void OnSelectedFilterChanged(ScheduleFilter? value) => _ = RefreshListAsync(CancellationToken.None);

    partial void OnSelectedChanged(ScheduleItem? value) => HasSelection = value is not null;

    private static DateTimeOffset RoundToHalfHour(DateTimeOffset value)
    {
        var local = value.ToLocalTime();
        var minute = local.Minute < 30 ? 30 : 0;
        var carry = local.Minute >= 30 ? 1 : 0;
        return new DateTimeOffset(
            local.Date.AddHours(local.Hour + carry).AddMinutes(minute),
            TimeZoneInfo.Local.GetUtcOffset(local.Date.AddHours(local.Hour + carry)));
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
            var items = await _scheduleStore.ListAsync(
                new ScheduleQuery
                {
                    State = SelectedFilter?.State,
                    IncludeDone = SelectedFilter is null
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
            _log.Error("加载日程失败。", ex);
            NoticeText = $"加载失败：{ex.Message}";
        }
    }
}