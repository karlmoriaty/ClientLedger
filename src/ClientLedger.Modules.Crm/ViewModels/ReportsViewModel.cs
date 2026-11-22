using System.Collections.ObjectModel;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Data;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Shell.ViewModels;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClientLedger.Modules.Crm.ViewModels;

public sealed record TrendBar(string Label, int Count, decimal Percent);

public sealed record MonthBar(string Label, decimal Amount, int Count, decimal Percent);

/// <summary>
/// 报表。M0 只做回答经营问题的四张图，不做自定义报表：
/// 客户漏斗、跟进活跃度、报价转化、成交趋势。
/// </summary>
public sealed partial class ReportsViewModel : PageViewModel
{
    private readonly ReportStore _reportStore;
    private readonly IAppLog _log;

    public ReportsViewModel(ReportStore reportStore, IAppLog log)
    {
        _reportStore = reportStore;
        _log = log;
    }

    public override string Title => "报表";

    public override string Subtitle => "漏斗、活动、转化与成交";

    public ObservableCollection<FunnelItem> Funnel { get; } = [];

    public ObservableCollection<TrendBar> Trend { get; } = [];

    public ObservableCollection<MonthBar> Monthly { get; } = [];

    [ObservableProperty]
    private int _quoteTotal;

    [ObservableProperty]
    private int _quoteAccepted;

    [ObservableProperty]
    private int _quoteRejected;

    [ObservableProperty]
    private int _quotePending;

    [ObservableProperty]
    private decimal _acceptedAmount;

    [ObservableProperty]
    private string _winRateText = "0%";

    [ObservableProperty]
    private string _trendSummary = "暂无跟进记录";

    public override async Task ActivateAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var funnel = await _reportStore.FunnelAsync(cancellationToken).ConfigureAwait(true);
            Funnel.Clear();
            foreach (var item in funnel)
            {
                Funnel.Add(item);
            }

            var trend = await _reportStore.ActivityTrendAsync(8, cancellationToken).ConfigureAwait(true);
            var trendMax = Math.Max(1, trend.Count == 0 ? 1 : trend.Max(point => point.Count));

            Trend.Clear();
            foreach (var point in trend)
            {
                Trend.Add(new TrendBar(point.Label, point.Count, point.Count * 100m / trendMax));
            }

            TrendSummary = trend.Sum(point => point.Count) == 0
                ? "最近 8 周暂无跟进记录"
                : $"最近 8 周共 {trend.Sum(point => point.Count)} 条跟进";

            var pipeline = await _reportStore.QuotePipelineAsync(cancellationToken).ConfigureAwait(true);
            QuoteTotal = pipeline.Total;
            QuoteAccepted = pipeline.Accepted;
            QuoteRejected = pipeline.Rejected;
            QuotePending = pipeline.Pending;
            AcceptedAmount = pipeline.AcceptedAmount;
            WinRateText = Foundation.Platform.Formats.Percent(pipeline.WinRatePercent);

            var monthly = await _reportStore.AcceptedByMonthAsync(6, cancellationToken).ConfigureAwait(true);
            var monthMax = monthly.Count == 0
                ? 1m
                : Math.Max(1m, monthly.Max(item => item.Amount));

            Monthly.Clear();
            foreach (var item in monthly)
            {
                var percent = item.Amount * 100m / monthMax;
                Monthly.Add(new MonthBar(item.Label, item.Amount, item.Count, percent));
            }
        }
        catch (Exception ex)
        {
            _log.Error("加载报表失败。", ex);
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => ActivateAsync();
}