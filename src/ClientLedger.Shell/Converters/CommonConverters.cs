using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ClientLedger.Shell.Converters;

public sealed class BoolVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is true;
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Visible;
}

public sealed class NullVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hasValue = value is not null && value is not string text || value is string s && !string.IsNullOrWhiteSpace(s);
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            hasValue = !hasValue;
        }

        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class EmptyTextVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var empty = value is not string text || string.IsNullOrWhiteSpace(text);
        if (string.Equals(parameter as string, "invert", StringComparison.OrdinalIgnoreCase))
        {
            empty = !empty;
        }

        return empty ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>多个布尔值与逻辑：全部为真才显示。用于"已选中且不在编辑态"这类按钮可见性。</summary>
public sealed class AllTrueToVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.All(value => value is true) ? Visibility.Visible : Visibility.Collapsed;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        [Binding.DoNothing];
}

/// <summary>
/// 语义令牌 → 画刷。令牌只有 success / warning / danger / accent / neutral 五种，
/// 避免各页面自己挑颜色导致语义色失控。
/// </summary>
public sealed class TokenBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        Resolve(value as string, string.Equals(parameter as string, "soft", StringComparison.OrdinalIgnoreCase));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;

    internal static Brush Resolve(string? token, bool soft)
    {
        var key = (token ?? "neutral").ToLowerInvariant() switch
        {
            "success" => soft ? "SuccessSoft" : "Success",
            "warning" => soft ? "WarningSoft" : "Warning",
            "danger" => soft ? "DangerSoft" : "Danger",
            "accent" => soft ? "AccentSoft" : "Accent",
            _ => soft ? "NeutralSoft" : "Ink1"
        };

        return Application.Current.TryFindResource(key) as Brush ?? Brushes.Gray;
    }
}

/// <summary>浅底徽章专用画刷。与 TokenBrushConverter 分开声明，避免每处绑定都要传 ConverterParameter。</summary>
public sealed class TokenSoftBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        TokenBrushConverter.Resolve(value as string, soft: true);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>百分比 → 条宽（用于漏斗图）。ConverterParameter 传最大像素宽度。</summary>
public sealed class PercentWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var percent = value is decimal d ? (double)d : 0d;
        var max = 220d;
        if (parameter is string text && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            max = parsed;
        }

        return Math.Max(2d, Math.Round(max * Math.Clamp(percent, 0d, 100d) / 100d));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>数量 → 相对最大值的条宽，用于趋势柱状图。</summary>
public sealed class CountWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var count = values.Length > 0 && values[0] is int i ? i : 0;
        var max = values.Length > 1 && values[1] is int m ? m : 0;
        var height = 120d;
        if (parameter is string text && double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            height = parsed;
        }

        return max <= 0 ? 2d : Math.Max(2d, Math.Round(height * count / max));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        [Binding.DoNothing];
}

public sealed class MoneyTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is decimal amount ? Foundation.Platform.Formats.Money(amount) : "¥0.00";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}