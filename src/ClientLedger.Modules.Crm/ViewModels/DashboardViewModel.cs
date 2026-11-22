using System.Collections.ObjectModel;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Data;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;
using ClientLedger.Shell.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientLedger.Modules.Crm.ViewModels;

/// <summary>
/// 工作台。回答三个问题：现在有多少客户、今天要做什么、最近发生了什么。
/// 首屏信息密度决定了用户对"这软件能不能用"的第一判断。
/// </summary>
public sealed partial class DashboardViewModel : PageViewModel
{
    private readonly CustomerStore _customerStore;
    private readonly ScheduleStore _scheduleStore;
    private readonly ActivityStore _activityStore;
    private readonly ReportStore _reportStore;
    private readonly IClock _clock;
    private readonly IAppLog _log;

    public DashboardViewModel(
        CustomerStore customerStore,
        ScheduleStore scheduleStore,
        ActivityStore activityStore,
        ReportStore reportStore,
        IClock clock,
        IAppLog log)
    {
        _customerStore = customerStore;
        _scheduleStore = scheduleStore;
        _activityStore = activityStore;
        _reportStore = reportStore;
        _clock = clock;
        _log = log;
    }

    public override string Title => "工作台";

    public override string Subtitle => GreetingText;

    public ObservableCollection<ScheduleItem> TodaySchedules { get; } = [];

    public ObservableCollection<Activity> RecentActivities { get; } = [];

    public ObservableCollection<FunnelItem> Funnel { get; } = [];

    [ObservableProperty]
    private int _totalCustomers;

    [ObservableProperty]
    private int _newThisMonth;

    [ObservableProperty]
    private int _pendingFollowUps;

    [ObservableProperty]
    private int _wonThisMonth;

    [ObservableProperty]
    private string _greetingText = string.Empty;

    [ObservableProperty]
    private string _todayText = string.Empty;

    [ObservableProperty]
    private string _scheduleEmptyText = "今天没有排定的日程。";

    [ObservableProperty]
    private bool _hasSchedules;

    [ObservableProperty]
    private bool _hasActivities;

    public override async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        GreetingText = BuildGreeting();
        OnPropertyChanged(nameof(Subtitle));
        TodayText = BuildTodayText();

        try
        {
            var stats = await _customerStore.StatsAsync(cancellationToken).ConfigureAwait(true);
            TotalCustomers = stats.Total;
            NewThisMonth = stats.NewThisMonth;
            PendingFollowUps = stats.PendingFollowUps;
            WonThisMonth = stats.WonThisMonth;

            var today = _clock.TodayLocal;
            var startLocal = today.ToDateTime(TimeOnly.MinValue);
            var start = new DateTimeOffset(startLocal, TimeZoneInfo.Local.GetUtcOffset(startLocal));
            var end = start.AddDays(1);

            var schedules = await _scheduleStore.ListAsync(
                new ScheduleQuery { FromUtc = start, ToUtc = end, State = ScheduleState.Pending },
                cancellationToken).ConfigureAwait(true);

            TodaySchedules.Clear();
            foreach (var item in schedules)
            {
                TodaySchedules.Add(item);
            }

            HasSchedules = TodaySchedules.Count > 0;
            ScheduleEmptyText = HasSchedules ? string.Empty : "今天没有排定的日程，适合集中处理跟进。";

            var activities = await _activityStore.ListAsync(
                new ActivityQuery { Limit = 6 },
                cancellationToken).ConfigureAwait(true);

            RecentActivities.Clear();
            foreach (var item in activities)
            {
                RecentActivities.Add(item);
            }

            HasActivities = RecentActivities.Count > 0;

            var funnel = await _reportStore.FunnelAsync(cancellationToken).ConfigureAwait(true);
            Funnel.Clear();
            foreach (var item in funnel)
            {
                Funnel.Add(item);
            }
        }
        catch (Exception ex)
        {
            _log.Error("加载工作台失败。", ex);
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => ActivateAsync();

    private static string BuildGreeting()
    {
        var hour = DateTime.Now.Hour;
        var part = hour switch
        {
            < 6 => "夜深了",
            < 12 => "上午好",
            < 18 => "下午好",
            _ => "晚上好"
        };

        return $"{part}，{Environment.UserName}";
    }

    private string BuildTodayText()
    {
        var today = _clock.TodayLocal;
        var weekday = today.DayOfWeek switch
        {
            DayOfWeek.Monday => "星期一",
            DayOfWeek.Tuesday => "星期二",
            DayOfWeek.Wednesday => "星期三",
            DayOfWeek.Thursday => "星期四",
            DayOfWeek.Friday => "星期五",
            DayOfWeek.Saturday => "星期六",
            _ => "星期日"
        };

        return $"{today:yyyy-MM-dd} · {weekday}";
    }
}