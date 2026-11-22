using System.Windows;
using System.Windows.Media;
using System.Windows.Shell;

namespace ClientLedger.Shell.Controls;

/// <summary>
/// 自绘标题栏窗口基类。用 WindowChrome 保留系统的拖拽、贴边、双击最大化与缩放热区，
/// 只替换标题栏外观，避免 WindowStyle=None + AllowsTransparency 带来的性能与渲染问题。
/// 派生窗口通过 Style="{StaticResource ChromeWindowStyle}" 应用外观。
/// </summary>
public class ChromeWindow : Window
{
    public ChromeWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(this, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(this, TextHintingMode.Fixed);

        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false
        });
    }
}