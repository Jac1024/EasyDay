using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace EasyDay.App.Infrastructure;

/// <summary>
/// 把窗口嵌进桌面层：
/// 找到 Progman → SHELLDLL_DefView → SysListView32(FolderView)，把窗口 SetParent 上去，
/// 再加 WS_EX_TOOLWINDOW。这样窗口活在桌面层：被普通窗口遮挡，回到桌面时可见。
/// </summary>
public sealed class DesktopEmbedService : IDisposable
{
    private const int GwlExStyle = -20;
    private const uint GaParent = 1;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExAppWindow = 0x00040000;

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    private readonly DispatcherTimer _watchdog;
    private Window? _window;
    private IntPtr _hwnd;
    private IntPtr _host;
    private bool _disposed;

    public DesktopEmbedService()
    {
        _watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _watchdog.Tick += (_, _) => Verify();
    }

    public bool IsEmbedded { get; private set; }

    /// <summary>桌面宿主消失（资源管理器重启、关闭桌面图标）时触发。</summary>
    public event Action? HostLost;

    public bool Embed(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        var host = FindDesktopHost();
        if (host == IntPtr.Zero)
        {
            FileLog.Error("嵌入桌面失败：找不到桌面宿主");
            return false;
        }

        FileLog.Info($"准备嵌入桌面 hwnd=0x{hwnd:X} host=0x{host:X} hostClass={GetClassName(host)}");

        SetParent(hwnd, host);

        // 注意：对没有 WS_CHILD 的窗口，GetParent 返回的是 owner，会一直是 0；
        // 必须用 GetAncestor(GA_PARENT) 判断真实父窗口。
        var actualParent = GetAncestor(hwnd, GaParent);
        if (actualParent != host)
        {
            FileLog.Error($"嵌入桌面失败：实际父窗口 0x{actualParent:X}，期望 0x{host:X}，错误 {Marshal.GetLastWin32Error()}");
            return false;
        }

        var exStyle = GetWindowLong(hwnd, GwlExStyle);
        exStyle |= WsExToolWindow;
        exStyle &= ~WsExAppWindow;
        SetWindowLong(hwnd, GwlExStyle, exStyle);
        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        _window = window;
        _hwnd = hwnd;
        _host = host;
        IsEmbedded = true;
        _watchdog.Start();
        FileLog.Info($"已嵌入桌面 host=0x{host:X}");
        return true;
    }

    public void Detach()
    {
        _watchdog.Stop();

        if (!IsEmbedded || _hwnd == IntPtr.Zero)
        {
            IsEmbedded = false;
            return;
        }

        SetParent(_hwnd, IntPtr.Zero);

        var exStyle = GetWindowLong(_hwnd, GwlExStyle);
        exStyle &= ~WsExToolWindow;
        exStyle |= WsExAppWindow;
        SetWindowLong(_hwnd, GwlExStyle, exStyle);
        SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoZOrder | SwpNoActivate | SwpFrameChanged);

        IsEmbedded = false;
        _hwnd = IntPtr.Zero;
        _host = IntPtr.Zero;
        _window = null;
    }

    /// <summary>按 DIP 坐标移动（父窗口位于屏幕 0,0，所以 DIP 坐标≈屏幕坐标）。</summary>
    public void MoveTo(double leftDip, double topDip)
    {
        if (!IsEmbedded || _hwnd == IntPtr.Zero || _window is null)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        var x = (int)Math.Round(leftDip * dpi.DpiScaleX);
        var y = (int)Math.Round(topDip * dpi.DpiScaleY);

        SetWindowPos(_hwnd, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate);
    }

    public bool TryGetPosition(out double leftDip, out double topDip)
    {
        leftDip = 0;
        topDip = 0;

        if (!IsEmbedded || _hwnd == IntPtr.Zero || _window is null)
        {
            return false;
        }

        if (!GetWindowRect(_hwnd, out var rect))
        {
            return false;
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        leftDip = rect.Left / dpi.DpiScaleX;
        topDip = rect.Top / dpi.DpiScaleY;
        return true;
    }

    public bool TryGetBounds(out double leftDip, out double topDip, out double widthDip, out double heightDip)
    {
        leftDip = 0;
        topDip = 0;
        widthDip = 0;
        heightDip = 0;

        if (!IsEmbedded || _hwnd == IntPtr.Zero || _window is null)
        {
            return false;
        }

        if (!GetWindowRect(_hwnd, out var rect))
        {
            return false;
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        leftDip = rect.Left / dpi.DpiScaleX;
        topDip = rect.Top / dpi.DpiScaleY;
        widthDip = (rect.Right - rect.Left) / dpi.DpiScaleX;
        heightDip = (rect.Bottom - rect.Top) / dpi.DpiScaleY;
        return true;
    }

    /// <summary>同时设置位置和尺寸（嵌入模式下需要，因为 WPF 的 Left/Top 不再生效）。</summary>
    public void SetBounds(double leftDip, double topDip, double widthDip, double heightDip)
    {
        if (!IsEmbedded || _hwnd == IntPtr.Zero || _window is null)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(_window);
        var x = (int)Math.Round(leftDip * dpi.DpiScaleX);
        var y = (int)Math.Round(topDip * dpi.DpiScaleY);
        var w = Math.Max(1, (int)Math.Round(widthDip * dpi.DpiScaleX));
        var h = Math.Max(1, (int)Math.Round(heightDip * dpi.DpiScaleY));

        SetWindowPos(_hwnd, IntPtr.Zero, x, y, w, h, SwpNoZOrder | SwpNoActivate);
    }

    private void Verify()
    {
        if (!IsEmbedded)
        {
            return;
        }

        if (!IsWindow(_hwnd) || !IsWindow(_host))
        {
            FileLog.Info("桌面宿主已销毁，退出嵌入模式");
            Detach();
            HostLost?.Invoke();
            return;
        }

        var currentHost = FindDesktopHost();
        if (currentHost == IntPtr.Zero || currentHost != _host)
        {
            FileLog.Info("桌面宿主发生变化（资源管理器可能重启），重新嵌入");
            Detach();
            HostLost?.Invoke();
        }
    }

    private static IntPtr FindDesktopHost()
    {
        var progman = FindWindow("Progman", null);
        var defView = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);

        if (defView == IntPtr.Zero)
        {
            // 某些系统桌面挂在 WorkerW 下
            EnumWindows((h, _) =>
            {
                if (GetClassName(h) != "WorkerW")
                {
                    return true;
                }

                var view = FindWindowEx(h, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (view == IntPtr.Zero)
                {
                    return true;
                }

                defView = view;
                return false;
            }, IntPtr.Zero);
        }

        if (defView == IntPtr.Zero)
        {
            return progman;
        }

        var folderView = FindWindowEx(defView, IntPtr.Zero, "SysListView32", "FolderView");
        return folderView != IntPtr.Zero ? folderView : defView;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _watchdog.Stop();
        Detach();
    }

    private static string GetClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        GetClassNameNative(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? windowName);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassNameNative(IntPtr hwnd, StringBuilder name, int count);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetParent(IntPtr child, IntPtr newParent);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
