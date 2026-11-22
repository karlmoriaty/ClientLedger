using System.Globalization;

namespace ClientLedger.Foundation.Platform;

/// <summary>
/// 全局显示格式。集中在一处，避免各页面各写一套日期/金额格式导致观感割裂。
/// </summary>
public static class Formats
{
    public const string DayPattern = "yyyy-MM-dd";
    public const string MomentPattern = "yyyy-MM-dd HH:mm";
    public const string MoneyPattern = "#,0.00";

    public static string Day(DateTimeOffset value) =>
        value.ToLocalTime().ToString(DayPattern, CultureInfo.InvariantCulture);

    public static string Moment(DateTimeOffset value) =>
        value.ToLocalTime().ToString(MomentPattern, CultureInfo.InvariantCulture);

    public static string Money(decimal value) =>
        "¥" + value.ToString(MoneyPattern, CultureInfo.CurrentCulture);

    public static string Number(decimal value) =>
        value.ToString("#,0.##", CultureInfo.CurrentCulture);

    public static string Percent(decimal value) =>
        value.ToString("0.#", CultureInfo.CurrentCulture) + "%";

    /// <summary>今天 / 昨天 / 明天 / 3 天前 / 5 天后。</summary>
    public static string RelativeDay(DateTimeOffset utc, IClock clock)
    {
        var days = (DateOnly.FromDateTime(utc.ToLocalTime().DateTime) .DayNumber) - clock.TodayLocal.DayNumber;
        return days switch
        {
            0 => "今天",
            -1 => "昨天",
            1 => "明天",
            -2 => "前天",
            2 => "后天",
            < 0 => $"{Math.Abs(days)} 天前",
            _ => $"{days} 天后"
        };
    }

    public static string Duration(int minutes)
    {
        if (minutes <= 0)
        {
            return "不提醒";
        }

        if (minutes < 60)
        {
            return $"{minutes} 分钟前";
        }

        if (minutes % 1440 == 0)
        {
            return $"{minutes / 1440} 天前";
        }

        if (minutes % 60 == 0)
        {
            return $"{minutes / 60} 小时前";
        }

        return $"{minutes / 60} 小时 {minutes % 60} 分钟前";
    }
}