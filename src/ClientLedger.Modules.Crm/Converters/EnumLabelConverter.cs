using System.Globalization;
using System.Windows.Data;
using ClientLedger.Modules.Crm.Domain;

namespace ClientLedger.Modules.Crm.Converters;

/// <summary>把 CRM 各枚举翻成中文，供 ComboBox 与只读文本共用。</summary>
public sealed class EnumLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        CustomerStatus status => CrmLabels.Text(status),
        CustomerGrade grade => CrmLabels.Text(grade),
        ActivityKind kind => CrmLabels.Text(kind),
        ScheduleKind kind => CrmLabels.Text(kind),
        ScheduleState state => CrmLabels.Text(state),
        QuoteStatus status => CrmLabels.Text(status),
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>主联系人标记等布尔值 → 可见性。</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
/// <summary>日程状态 → 是否待完成，用于"完成"按钮的可见性。</summary>
public sealed class ScheduleStatePendingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ScheduleState state && state == ScheduleState.Pending;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>提醒分钟数 → 人话。</summary>
public sealed class RemindMinutesLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int minutes ? Foundation.Platform.Formats.Duration(minutes) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}