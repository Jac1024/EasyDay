using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using EasyDay.App.Infrastructure;
using EasyDay.App.ViewModels;
using EasyDay.App.Views;
using EasyDay.Core.Ai;
using EasyDay.Core.Data;
using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.App.Services;

/// <summary>应用外壳：负责窗口、托盘、热键、提醒之间的串联。</summary>
public sealed class AppShell : IDisposable
{
    private readonly EventStore _store;
    private readonly SettingsService _settings;
    private readonly AiEventParser _parser;
    private readonly List<ReminderToastWindow> _toasts = new();

    private WidgetWindow? _widget;
    private WidgetViewModel? _widgetViewModel;
    private TrayService? _tray;
    private HotkeyService? _hotkeys;
    private ReminderScheduler? _scheduler;
    private SettingsWindow? _settingsWindow;
    private bool _disposed;

    public AppShell(EventStore store, SettingsService settings, AiEventParser parser)
    {
        _store = store;
        _settings = settings;
        _parser = parser;
    }

    // 嵌入桌面时挂件是桌面的子窗口，不能作为对话框 Owner
    private Window? DialogOwner
        => _widget is { IsVisible: true, IsDesktopEmbedded: false } ? _widget : null;

    public void Start()
    {
        CreateWidget();
        CreateTray();
        RegisterHotkeys();
        StartScheduler();

        if (!_settings.Current.HasAiConfig)
        {
            FileLog.Info("首次使用：尚未配置 AI 接口");
            _tray?.ShowBalloon("EasyDay", "第一次使用：先填 AI 接口和 Key（约 1 分钟），之后就能识别群通知了。");
            _widget?.Dispatcher.BeginInvoke(new Action(OpenSettings), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    private void CreateWidget()
    {
        _widgetViewModel = new WidgetViewModel(_store);
        _widget = new WidgetWindow(_widgetViewModel, _settings);

        _widget.CreateRequested += date => CreateEvent(date?.ToDateTime(TimeOnly.MinValue));
        _widget.EditRequested += EditOccurrence;
        _widget.SettingsRequested += OpenSettings;
        _widget.QuickAddRequested += QuickAdd;
        _widget.CaptureRequested += Capture;
        _widget.ClipboardRequested += AiCaptureFromClipboard;
        _widget.AiCaptureRequested += AiCaptureFromClipboard;
        _widget.AssistantRequested += OpenAssistant;

        _widget.IsVisibleChanged += (_, _) => _tray?.UpdateVisible(_widget.IsVisible);

        _widget.Show();
    }

    private void CreateTray()
    {
        _tray = new TrayService(
            show: ShowWidget,
            newEvent: () => CreateEvent(null),
            quickAdd: QuickAdd,
            assistant: OpenAssistant,
            settings: OpenSettings,
            exit: Exit);
    }

    private void RegisterHotkeys()
    {
        _hotkeys = new HotkeyService();
        _hotkeys.Register(HotkeyService.ModControl | HotkeyService.ModAlt, 0x20, QuickAdd, "Ctrl+Alt+Space 速记");
        _hotkeys.Register(HotkeyService.ModControl | HotkeyService.ModAlt, 0x56, AiCaptureFromClipboard, "Ctrl+Alt+V 剪贴板识别");
        _hotkeys.Register(HotkeyService.ModAlt | HotkeyService.ModShift, 0x41, Capture, "Alt+Shift+A 截图识别");
        _hotkeys.Register(HotkeyService.ModControl | HotkeyService.ModAlt, 0x51, OpenAssistant, "Ctrl+Alt+Q 问问助手");
    }

    private void StartScheduler()
    {
        _scheduler = new ReminderScheduler(_store, _settings, ShowReminder, ShowBrief);
        _scheduler.Start();
    }

    public void ShowWidget()
    {
        if (_widget is null)
        {
            return;
        }

        _widget.Show();
        if (_widget.WindowState == WindowState.Minimized)
        {
            _widget.WindowState = WindowState.Normal;
        }

        _widget.Activate();
        _widget.Refresh();
        _tray?.UpdateVisible(true);
    }

    private void RefreshWidget()
    {
        _widget?.Refresh();
    }

    private void CreateEvent(DateTime? date)
    {
        var dialog = new EventEditorWindow(_store, _settings, null, date)
        {
            Owner = DialogOwner
        };

        if (dialog.ShowDialog() == true)
        {
            RefreshWidget();
        }
    }

    private void EditOccurrence(EventOccurrence occurrence)
    {
        var dialog = new EventEditorWindow(_store, _settings, occurrence.Source, null)
        {
            Owner = DialogOwner
        };

        if (dialog.ShowDialog() == true)
        {
            RefreshWidget();
        }
    }

    private void OpenSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        var dialog = new SettingsWindow(_settings, _store);
        _settingsWindow = dialog;

        if (dialog.ShowDialog() == true)
        {
            _widget?.ApplySettings();
            RefreshWidget();
        }

        _settingsWindow = null;
    }

    private void QuickAdd() => AiCaptureFromClipboard();

    /// <summary>自然语言助手：查空闲、改期、取消。改期/取消需要用户在窗口里再确认一次。</summary>
    private void OpenAssistant()
    {
        var dialog = new AssistantWindow(_store, _settings)
        {
            Owner = DialogOwner
        };

        dialog.CreateRequested += text =>
        {
            dialog.Close();
            _ = ParseAndShowAsync(text, null, EventSource.Manual);
        };

        dialog.Changed += RefreshWidget;
        dialog.ShowDialog();
    }

    /// <summary>
    /// AI 识别入口：
    /// - 剪贴板是图片 → 直接识别
    /// - 剪贴板是文字 → 打开速记窗并自动填入，用户可改可清空
    /// </summary>
    private async void AiCaptureFromClipboard()
    {
        try
        {
            var image = TryReadClipboardImage();
            if (image is not null)
            {
                await ParseAndShowAsync(null, image, EventSource.Clipboard);
                return;
            }

            var text = Clipboard.ContainsText() ? Clipboard.GetText() : null;
            var fromClipboard = !string.IsNullOrWhiteSpace(text);

            if (!fromClipboard)
            {
                _tray?.ShowBalloon("EasyDay", "剪贴板是空的：先复制一段群通知，再点「AI 识别剪贴板」。");
            }

            var dialog = new QuickAddWindow(fromClipboard ? text : null)
            {
                Owner = DialogOwner
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            await ParseAndShowAsync(
                dialog.InputText,
                null,
                fromClipboard ? EventSource.Clipboard : EventSource.QuickAdd);
        }
        catch (Exception ex)
        {
            FileLog.Error("AI 识别入口失败", ex);
            MessageBox.Show("识别失败：" + ex.Message, "EasyDay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void Capture()
    {
        try
        {
            var overlay = new CaptureOverlayWindow();
            overlay.ShowDialog();

            if (overlay.Result is { Length: > 0 } bytes)
            {
                await ParseAndShowAsync(null, bytes, EventSource.Screenshot);
            }
        }
        catch (Exception ex)
        {
            FileLog.Error("截图识别失败", ex);
            MessageBox.Show("截图失败：" + ex.Message, "EasyDay", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task ParseAndShowAsync(string? text, byte[]? image, EventSource source, string? extraContext = null)
    {
        var busy = new BusyWindow(image is null ? "AI 解析中…" : "AI 识别截图中…");
        busy.Show();

        ParseOutcome outcome;
        try
        {
            outcome = await _parser.ParseAsync(text, image, source, DateTime.Now, extraContext);
        }
        catch (Exception ex)
        {
            FileLog.Error("解析异常", ex);
            MessageBox.Show("解析失败：" + ex.Message, "EasyDay", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        finally
        {
            busy.Close();
        }

        if (outcome.Drafts.Count == 0)
        {
            MessageBox.Show(
                outcome.Error ?? "没有识别到日程，换一段更完整的文字或截图试试。",
                "EasyDay",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (!string.IsNullOrWhiteSpace(outcome.Error))
        {
            FileLog.Error("AI 解析：" + outcome.Error);
        }

        var dialog = new DraftCardWindow(outcome.Drafts, outcome.Error, _store.GetAll());
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        if (dialog.ReParseRequested)
        {
            var extra = string.Join("\n", dialog.Answers.Select(a => $"- 问：{a.Question} 答：{a.Answer}"));
            await ParseAndShowAsync(text, image, source, extra);
            return;
        }

        foreach (var draft in dialog.Result)
        {
            _store.Add(draft);
        }

        RefreshWidget();
        _tray?.ShowBalloon("已加入日历", $"新增 {dialog.Result.Count} 条日程");
    }

    private void ShowReminder(Reminder reminder)
    {
        var toast = new ReminderToastWindow(reminder);

        toast.CompleteRequested += item =>
        {
            if (item.Event is not null)
            {
                _store.SetStatus(item.Event.Id, EventStatus.Completed);
            }

            RefreshWidget();
        };

        toast.SnoozeRequested += item => _store.Snooze(item.Id, 10);

        toast.OpenRequested += item =>
        {
            ShowWidget();
            if (item.Event is null)
            {
                return;
            }

            var calendarEvent = _store.Get(item.Event.Id);
            if (calendarEvent is null)
            {
                return;
            }

            var dialog = new EventEditorWindow(_store, _settings, calendarEvent, null);
            if (dialog.ShowDialog() == true)
            {
                RefreshWidget();
            }
        };

        toast.Closed += (_, _) =>
        {
            _toasts.Remove(toast);
            PositionToasts();
        };

        _toasts.Add(toast);
        while (_toasts.Count > 3)
        {
            _toasts[0].Close();
        }

        toast.Show();
        PositionToasts();
    }

    private void PositionToasts()
    {
        if (_toasts.Count == 0)
        {
            return;
        }

        var work = ScreenUtil.PrimaryWorkArea(_toasts[0]);
        double offset = 0;
        foreach (var toast in _toasts.ToList())
        {
            toast.SetPosition(work.Right, work.Bottom, offset);
            offset += toast.Height - 12;
        }
    }

    private void ShowBrief(string title, string text)
    {
        _tray?.ShowBalloon(title, text);
    }

    private static byte[]? TryReadClipboardImage()
    {
        try
        {
            if (!Clipboard.ContainsImage())
            {
                return null;
            }

            var source = Clipboard.GetImage();
            if (source is null)
            {
                return null;
            }

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch (Exception ex)
        {
            FileLog.Error("读取剪贴板图片失败", ex);
            return null;
        }
    }

    public void Exit()
    {
        if (_disposed)
        {
            return;
        }

        FileLog.Info("退出应用");
        _widget?.PrepareForExit();
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scheduler?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();

        foreach (var toast in _toasts.ToList())
        {
            toast.Close();
        }

        _toasts.Clear();
    }
}
