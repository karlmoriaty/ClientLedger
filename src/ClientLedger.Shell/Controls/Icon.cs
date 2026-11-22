using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClientLedger.Shell.Controls;

/// <summary>
/// 线性图标控件。Geometry 取自 24×24 网格，由 Viewbox 等比缩放，
/// 保证不同图标的视觉重量一致（直接用 Path 的 Stretch 会因图形自身包围盒不同而大小不一）。
/// </summary>
public sealed class Icon : Control
{
    public static readonly DependencyProperty GeometryProperty = DependencyProperty.Register(
        nameof(Geometry),
        typeof(Geometry),
        typeof(Icon),
        new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(Icon),
        new FrameworkPropertyMetadata(18d));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness),
        typeof(double),
        typeof(Icon),
        new FrameworkPropertyMetadata(2d));

    public Geometry? Geometry
    {
        get => (Geometry?)GetValue(GeometryProperty);
        set => SetValue(GeometryProperty, value);
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }
}