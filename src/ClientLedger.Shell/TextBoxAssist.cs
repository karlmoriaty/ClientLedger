using System.Windows;

namespace ClientLedger.Shell;

/// <summary>
/// 给 WPF TextBox 补一个 Placeholder 附加属性。
/// WPF 原生不支持占位文案，而占位文案是表单可读性的一部分，不能省。
/// </summary>
public static class TextBoxAssist
{
    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached(
        "Placeholder",
        typeof(string),
        typeof(TextBoxAssist),
        new FrameworkPropertyMetadata(string.Empty));

    public static string GetPlaceholder(DependencyObject element) =>
        (string)element.GetValue(PlaceholderProperty);

    public static void SetPlaceholder(DependencyObject element, string value) =>
        element.SetValue(PlaceholderProperty, value);
}