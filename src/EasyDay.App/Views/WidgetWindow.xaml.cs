using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using EasyDay.App.Infrastructure;
using EasyDay.App.ViewModels;
using EasyDay.Core.Data;

namespace EasyDay.App.Views;

public partial class WidgetWindow : Window
{
    private const double SnapDistance = 24;
    private const double MinWidgetWidth = 320;
    private const double MinWidgetHeight = 520;
    private const double CollapsedHeight = 320;

    private readonly WidgetViewModel _viewModel;
    private readonly SettingsService _settings;
    private readonly DesktopEmbedService _embed = new();

    private bool _allowClose;
    private bool _dragging;
    private CursorPoint _dragCursorStart;
    private double _dragWindowLeft;
    private double _dragWindowTop;

    private bool _resizing;
    private ResizeEdge _resizeEdge;
    private CursorPoint _resizeCursorStart;
    private double _resizeStartLeft;
    private double _resizeStartTop;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    public WidgetWindow(WidgetViewModel viewModel, SettingsService settings)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _settings = settings;
        DataContext = viewModel;

        _viewModel.CreateRequested += date => CreateRequested?.Invoke(date);
        _viewModel.EditRequested += occurrence => EditRequested?.Invoke(occurrence);
        _viewModel.SettingsRequested += () => SettingsRequested?.Invoke();
        _viewModel.AiCaptureRequested += () => AiCaptureRequested?.Invoke();

        _embed.HostLost += OnDesktopHostLost;

        SizeChanged += (_, _) => ApplyRoundedRegion();

        _viewModel.IsCollapsed = _settings.Current.WidgetCollapsed;
        UpdateCollapseButton();

        PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is not System.Windows.Controls.TextBox)
            {
                Focus();
            }
        };
    }

    public event Action<DateOnly?>? CreateRequested;

    public event Action<EasyDay.Core.Scheduling.EventOccurrence>? EditRequested;

    public event Action? SettingsRequested;

    public event Action? QuickAddRequested;

    public event Action? CaptureRequested;

    public event Action? ClipboardRequested;

    public event Action? AiCaptureRequested;

    public event Action? AssistantRequested;

    public bool IsDesktopEmbedded => _embed.IsEmbedded;

    // ---------- 显示方式 ----------

    public void ApplySettings()
    {
        FitToWorkArea();
        Opacity = 1;

        var mode = NormalizeMode(_settings.Current.WidgetMode);

        if (mode == "desktop")
        {
            if (_embed.IsEmbedded || _embed.Embed(this))
            {
                Topmost = false;
                var (left, top) = GetSavedOrDefaultPosition();
                _embed.SetBounds(left, top, Width, Height);
                UpdateModeChecks("desktop");
                return;
            }

            // 嵌入失败（桌面宿主找不到）就退回普通窗口
            FileLog.Error("嵌入桌面失败，已回退为普通窗口");
            _settings.Current.WidgetMode = "floating";
            _settings.Save();
            mode = "floating";
        }

        if (_embed.IsEmbedded)
        {
            _embed.Detach();
        }

        Topmost = mode == "topmost";

        var (savedLeft, savedTop) = GetSavedOrDefaultPosition();
        SetPosition(savedLeft, savedTop);
        ClampToScreen();
        UpdateModeChecks(mode);
    }

    private void OnDesktopHostLost()
    {
        if (NormalizeMode(_settings.Current.WidgetMode) != "desktop")
        {
            return;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            FileLog.Info("恢复桌面嵌入");
            ApplySettings();
        }), DispatcherPriority.Background);
    }

    private static string NormalizeMode(string? mode)
        => mode is "floating" or "topmost" or "desktop" ? mode : "floating";

    private void UpdateModeChecks(string mode)
    {
        ModeFloatingItem.IsChecked = mode == "floating";
        ModeTopmostItem.IsChecked = mode == "topmost";
        ModeDesktopItem.IsChecked = mode == "desktop";
    }

    private void Mode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.MenuItem { Tag: string mode })
        {
            _settings.Current.WidgetMode = mode;
            _settings.Save();
            ApplySettings();
        }
    }

    // ---------- 位置 ----------

    private (double Left, double Top) GetSavedOrDefaultPosition()
    {
        var work = ScreenUtil.PrimaryWorkArea(this);

        if (_settings.Current.WidgetLeft is { } left &&
            _settings.Current.WidgetTop is { } top &&
            IsPositionOnScreen(left, top))
        {
            return (
                Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - Width)),
                Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - Height)));
        }

        return (work.Right - Width - 24, work.Bottom - Height - 24);
    }

    private void SetPosition(double left, double top)
    {
        if (_embed.IsEmbedded)
        {
            _embed.MoveTo(left, top);
            return;
        }

        Left = left;
        Top = top;
    }

    private (double Left, double Top) GetPosition()
    {
        if (_embed.IsEmbedded && _embed.TryGetPosition(out var left, out var top))
        {
            return (left, top);
        }

        return (Left, Top);
    }

    private (double Left, double Top, double Width, double Height) GetBounds()
    {
        if (_embed.IsEmbedded && _embed.TryGetBounds(out var left, out var top, out var width, out var height))
        {
            return (left, top, width, height);
        }

        return (Left, Top, Width, Height);
    }

    private void ApplyBounds(double left, double top, double width, double height)
    {
        width = Math.Max(MinWidgetWidth, width);
        height = Math.Max(EffectiveMinHeight, height);

        if (_embed.IsEmbedded)
        {
            _embed.SetBounds(left, top, width, height);
            return;
        }

        Left = left;
        Top = top;
        Width = width;
        Height = height;
    }

    private double EffectiveMinHeight => _viewModel.IsCollapsed ? 240 : MinWidgetHeight;

    public void SavePosition()
    {
        var (left, top, width, height) = GetBounds();
        _settings.Current.WidgetLeft = left;
        _settings.Current.WidgetTop = top;
        _settings.Current.WidgetWidth = width;

        // 收起状态下不覆盖展开时的高度
        if (!_viewModel.IsCollapsed)
        {
            _settings.Current.WidgetHeight = height;
        }

        _settings.Save();
    }

    private void FitToWorkArea()
    {
        var work = ScreenUtil.PrimaryWorkArea(this);
        var width = _settings.Current.WidgetWidth ?? 380;
        var height = _viewModel.IsCollapsed
            ? CollapsedHeight
            : _settings.Current.WidgetHeight ?? 640;

        Width = Math.Clamp(width, MinWidgetWidth, Math.Max(MinWidgetWidth, work.Width - 24));
        Height = Math.Clamp(
            height,
            _viewModel.IsCollapsed ? 240 : MinWidgetHeight,
            Math.Max(240, work.Height - 24));
    }

    private void ClampToScreen()
    {
        var work = ScreenUtil.PrimaryWorkArea(this);
        var (left, top, width, height) = GetBounds();
        left = Math.Clamp(left, work.Left, Math.Max(work.Left, work.Right - width));
        top = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - height));
        ApplyBounds(left, top, width, height);
    }

    private bool IsPositionOnScreen(double left, double top)
    {
        var work = ScreenUtil.PrimaryWorkArea(this);
        return left > work.Left - 200
               && left < work.Right - 100
               && top > work.Top - 50
               && top < work.Bottom - 100;
    }

    private void SnapToEdges()
    {
        var work = ScreenUtil.PrimaryWorkArea(this);
        var (left, top, width, height) = GetBounds();

        if (Math.Abs(left - work.Left) < SnapDistance)
        {
            left = work.Left;
        }
        else if (Math.Abs(left - (work.Right - width)) < SnapDistance)
        {
            left = work.Right - width;
        }

        if (Math.Abs(top - work.Top) < SnapDistance)
        {
            top = work.Top;
        }
        else if (Math.Abs(top - (work.Bottom - height)) < SnapDistance)
        {
            top = work.Bottom - height;
        }

        left = Math.Clamp(left, work.Left - width + 80, work.Right - 80);
        top = Math.Clamp(top, work.Top - 10, work.Bottom - 80);
        ApplyBounds(left, top, width, height);
    }

    // ---------- 窗口事件 ----------

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        ApplySettings();
        _viewModel.Refresh();
        ApplyRoundedRegion();

        // 进程启动时 DPI 可能还没切换完（96 → 192），等空闲再布局一次
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ApplySettings();
            ApplyRoundedRegion();
            FileLog.Info($"挂件已加载 left={Left} top={Top} {ActualWidth}x{ActualHeight} " +
                         $"dpi={VisualTreeHelper.GetDpi(this).PixelsPerDip} embedded={_embed.IsEmbedded}");
        }), DispatcherPriority.ApplicationIdle);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplySettings();
        ApplyRoundedRegion();
    }

    /// <summary>
    /// 桌面层不合成 WPF 的分层窗口，所以挂件改为不透明窗口 + 圆角 Region。
    /// </summary>
    private void ApplyRoundedRegion()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Round(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Round(ActualHeight * dpi.DpiScaleY);
        var diameter = (int)Math.Round(28 * dpi.DpiScaleX);

        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, diameter, diameter);
        SetWindowRgn(hwnd, region, true);
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (_embed.IsEmbedded)
        {
            if (!GetCursorPos(out _dragCursorStart))
            {
                return;
            }

            var (left, top) = GetPosition();
            _dragWindowLeft = left;
            _dragWindowTop = top;
            _dragging = true;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        SnapToEdges();
        SavePosition();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_resizing)
        {
            UpdateResize();
            return;
        }

        if (!_dragging || !_embed.IsEmbedded || !GetCursorPos(out var cursor))
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (cursor.X - _dragCursorStart.X) / dpi.DpiScaleX;
        var dy = (cursor.Y - _dragCursorStart.Y) / dpi.DpiScaleY;
        _embed.MoveTo(_dragWindowLeft + dx, _dragWindowTop + dy);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (_resizing)
        {
            _resizing = false;
            ReleaseMouseCapture();
            SavePosition();
            return;
        }

        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();
        SnapToEdges();
        SavePosition();
    }

    // ---------- 缩放 ----------

    private void ResizeZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left ||
            sender is not FrameworkElement { Tag: string tag } ||
            !Enum.TryParse<ResizeEdge>(tag, out var edge) ||
            !GetCursorPos(out _resizeCursorStart))
        {
            return;
        }

        var (left, top, width, height) = GetBounds();
        _resizeStartLeft = left;
        _resizeStartTop = top;
        _resizeStartWidth = width;
        _resizeStartHeight = height;
        _resizeEdge = edge;
        _resizing = true;
        CaptureMouse();
        e.Handled = true;
    }

    private void UpdateResize()
    {
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (cursor.X - _resizeCursorStart.X) / dpi.DpiScaleX;
        var dy = (cursor.Y - _resizeCursorStart.Y) / dpi.DpiScaleY;
        var work = ScreenUtil.PrimaryWorkArea(this);

        var left = _resizeStartLeft;
        var top = _resizeStartTop;
        var width = _resizeStartWidth;
        var height = _resizeStartHeight;

        if (_resizeEdge is ResizeEdge.Left or ResizeEdge.TopLeft or ResizeEdge.BottomLeft)
        {
            var newWidth = Math.Clamp(
                _resizeStartWidth - dx,
                MinWidgetWidth,
                Math.Max(MinWidgetWidth, _resizeStartLeft + _resizeStartWidth - work.Left));
            left = _resizeStartLeft + (_resizeStartWidth - newWidth);
            width = newWidth;
        }
        else if (_resizeEdge is ResizeEdge.Right or ResizeEdge.TopRight or ResizeEdge.BottomRight)
        {
            width = Math.Clamp(
                _resizeStartWidth + dx,
                MinWidgetWidth,
                Math.Max(MinWidgetWidth, work.Right - _resizeStartLeft));
        }

        if (_resizeEdge is ResizeEdge.Top or ResizeEdge.TopLeft or ResizeEdge.TopRight)
        {
            var newHeight = Math.Clamp(
                _resizeStartHeight - dy,
                EffectiveMinHeight,
                Math.Max(EffectiveMinHeight, _resizeStartTop + _resizeStartHeight - work.Top));
            top = _resizeStartTop + (_resizeStartHeight - newHeight);
            height = newHeight;
        }
        else if (_resizeEdge is ResizeEdge.Bottom or ResizeEdge.BottomLeft or ResizeEdge.BottomRight)
        {
            height = Math.Clamp(
                _resizeStartHeight + dy,
                EffectiveMinHeight,
                Math.Max(EffectiveMinHeight, work.Bottom - _resizeStartTop));
        }

        ApplyBounds(left, top, width, height);
    }

    private void Window_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (AgendaScroll.IsMouseOver)
        {
            return;
        }

        if (e.Delta > 0)
        {
            _viewModel.PrevMonthCommand.Execute(null);
        }
        else if (e.Delta < 0)
        {
            _viewModel.NextMonthCommand.Execute(null);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    public void Refresh()
    {
        _viewModel.Refresh();
    }

    public void PrepareForExit()
    {
        _allowClose = true;
        SavePosition();
        _embed.Dispose();
    }

    private void Opacity_Click(object sender, RoutedEventArgs e) => ApplyRoundedRegion();
    private void QuickAdd_Click(object sender, RoutedEventArgs e) => QuickAddRequested?.Invoke();

    private void Capture_Click(object sender, RoutedEventArgs e) => CaptureRequested?.Invoke();

    private void Clipboard_Click(object sender, RoutedEventArgs e) => ClipboardRequested?.Invoke();

    private void Assistant_Click(object sender, RoutedEventArgs e) => AssistantRequested?.Invoke();

    private void Settings_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();

    private void Hide_Click(object sender, RoutedEventArgs e)
    {
        FileLog.Info("隐藏挂件到托盘");
        Hide();
    }

    private void ToggleCollapse_Click(object sender, RoutedEventArgs e)
    {
        var collapsed = !_viewModel.IsCollapsed;
        var (left, top, width, height) = GetBounds();
        var work = ScreenUtil.PrimaryWorkArea(this);

        double targetHeight;
        if (collapsed)
        {
            _settings.Current.WidgetHeight = height;
            targetHeight = Math.Min(CollapsedHeight, Math.Max(240, work.Height - 24));
        }
        else
        {
            targetHeight = Math.Clamp(
                _settings.Current.WidgetHeight ?? 640,
                MinWidgetHeight,
                Math.Max(MinWidgetHeight, work.Height - 24));
        }

        _viewModel.IsCollapsed = collapsed;
        _settings.Current.WidgetCollapsed = collapsed;
        UpdateCollapseButton();

        // 保持上边缘不动：日历向上收起，窗口从底部收缩
        var newTop = Math.Clamp(top, work.Top, Math.Max(work.Top, work.Bottom - targetHeight));
        ApplyBounds(left, newTop, width, targetHeight);
        _settings.Save();
    }

    private void UpdateCollapseButton()
    {
        CollapseButton.Content = _viewModel.IsCollapsed ? "▾" : "▴";
        CollapseButton.ToolTip = _viewModel.IsCollapsed ? "展开日历" : "收起日历";
    }

    private void Exit_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            "确定要完全退出 EasyDay 吗？退出后将不再提醒。",
            "退出 EasyDay",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        FileLog.Info("用户从挂件退出");
        Application.Current.Shutdown();
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out CursorPoint point);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hwnd, IntPtr region, bool redraw);

    [StructLayout(LayoutKind.Sequential)]
    private struct CursorPoint
    {
        public int X;
        public int Y;
    }

    private enum ResizeEdge
    {
        None,
        Left,
        Right,
        Top,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }
}
