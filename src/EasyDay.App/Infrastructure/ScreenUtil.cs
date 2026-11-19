using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace EasyDay.App.Infrastructure;

/// <summary>
/// 取屏幕尺寸。WPF 的 SystemParameters 在混合 DPI / 高缩放下会给错值，
/// 这里统一用 Win32/屏幕信息拿到物理像素，再按窗口 DPI 换算成 DIP。
/// </summary>
public static class ScreenUtil
{
    public static Rect PrimaryWorkArea(Visual visual)
    {
        var dpi = VisualTreeHelper.GetDpi(visual);
        var screen = WF.Screen.PrimaryScreen ?? WF.Screen.AllScreens[0];
        var area = screen.WorkingArea;

        return new Rect(
            area.X / dpi.DpiScaleX,
            area.Y / dpi.DpiScaleY,
            area.Width / dpi.DpiScaleX,
            area.Height / dpi.DpiScaleY);
    }

    public static (int X, int Y, int Width, int Height) VirtualScreen()
        => (GetSystemMetrics(76), GetSystemMetrics(77), GetSystemMetrics(78), GetSystemMetrics(79));

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
