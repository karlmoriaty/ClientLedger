using System.Globalization;

namespace ClientLedger.Modules.Crm.Domain;

/// <summary>
/// 日期输入的唯一解析口径。界面上用文本框而不是 DatePicker：
/// CRM 的录入频率高，键盘输入 2026-11-24 14:30 比在日历里点两次更快，
/// 也让样式完全受控（WPF DatePicker 的日历弹层很难做出高级感）。
/// </summary>
public static class DateInput
{
    public const string MomentFormat = "yyyy-MM-dd HH:mm";
    public const string DayFormat = "yyyy-MM-dd";

    public static string Moment(DateTimeOffset value) =>
        value.ToLocalTime().ToString(MomentFormat, CultureInfo.InvariantCulture);

    public static string Day(DateTimeOffset value) =>
        value.ToLocalTime().ToString(DayFormat, CultureInfo.InvariantCulture);

    public static DateTimeOffset ParseMomentOrNow(string? text) =>
        ParseMomentOrNull(text) ?? DateTimeOffset.UtcNow;

    public static DateTimeOffset? ParseMomentOrNull(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var local)
            ? new DateTimeOffset(local)
            : null;
    }

    public static DateTimeOffset? ParseDayOrNull(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var local)
            ? new DateTimeOffset(local.Date)
            : null;
    }
}