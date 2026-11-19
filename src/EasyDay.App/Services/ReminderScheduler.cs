using System.Windows.Threading;
using EasyDay.App.Infrastructure;
using EasyDay.Core.Data;
using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;
using Microsoft.Win32;

namespace EasyDay.App.Services;

/// <summary>提醒与每日简报。每分钟检查一次；休眠唤醒后补检查。</summary>
public sealed class ReminderScheduler : IDisposable
{
    private readonly EventStore _store;
    private readonly SettingsService _settings;
    private readonly Action<Reminder> _onReminder;
    private readonly Action<string, string> _onBrief;
    private readonly DispatcherTimer _timer;
    private bool _disposed;

    public ReminderScheduler(
        EventStore store,
        SettingsService settings,
        Action<Reminder> onReminder,
        Action<string, string> onBrief)
    {
        _store = store;
        _settings = settings;
        _onReminder = onReminder;
        _onBrief = onBrief;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _timer.Tick += (_, _) => Tick();
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Start()
    {
        _timer.Start();
        Tick();
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            Tick();
        }
    }

    private void Tick()
    {
        var now = DateTime.Now;

        try
        {
            FireReminders(now);
            CheckBriefs(now);
        }
        catch (Exception ex)
        {
            FileLog.Error("提醒调度失败", ex);
        }
    }

    private void FireReminders(DateTime now)
    {
        var due = _store.GetDueReminders(now);
        foreach (var reminder in due)
        {
            if (reminder.FireAt < now.AddHours(-12))
            {
                // 电脑关了太久，静默标记，不补一堆弹窗
                _store.MarkFired(reminder.Id);
                continue;
            }

            _store.MarkFired(reminder.Id);

            if (_settings.Current.EnableReminderToasts)
            {
                _onReminder(reminder);
            }
        }
    }

    private void CheckBriefs(DateTime now)
    {
        var settings = _settings.Current;
        var todayKey = now.ToString("yyyy-MM-dd");

        if (settings.EnableMorningBrief && now.Hour == 8 && now.Minute >= 30 && settings.LastMorningBrief != todayKey)
        {
            settings.LastMorningBrief = todayKey;
            _settings.Save();

            var events = RecurrenceExpander.ForDay(_store.GetAll(), DateOnly.FromDateTime(now));
            var text = events.Count == 0
                ? "今天没有安排，适合处理积压的事。"
                : $"今天 {events.Count} 项安排，第一个 {events[0].Start:HH:mm} {events[0].Source.Title}。";
            _onBrief("今日安排", text);
        }

        if (settings.EnableEveningBrief && now.Hour == 20 && settings.LastEveningBrief != todayKey)
        {
            settings.LastEveningBrief = todayKey;
            _settings.Save();

            var tomorrow = DateOnly.FromDateTime(now.AddDays(1));
            var events = RecurrenceExpander.ForDay(_store.GetAll(), tomorrow);
            var text = events.Count == 0
                ? "明天暂时没有安排。"
                : $"明天 {events.Count} 项安排，第一个 {events[0].Start:HH:mm} {events[0].Source.Title}。";
            _onBrief("明日安排", text);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Stop();
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
    }
}
