using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasyDay.App.Infrastructure;
using EasyDay.Core.Data;
using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.App.Views;

public partial class EventEditorWindow : Window
{
    private readonly EventStore _store;
    private readonly SettingsService _settings;
    private readonly CalendarEvent? _existing;

    public EventEditorWindow(
        EventStore store,
        SettingsService settings,
        CalendarEvent? existing,
        DateTime? initialDate)
    {
        InitializeComponent();
        _store = store;
        _settings = settings;
        _existing = existing;

        if (existing is null)
        {
            InitializeNew(initialDate);
        }
        else
        {
            LoadExisting(existing);
        }
    }

    public bool Saved { get; private set; }

    private void InitializeNew(DateTime? initialDate)
    {
        HeaderText.Text = "新建日程";

        var start = initialDate ?? DefaultStart();
        if (start.TimeOfDay == TimeSpan.Zero)
        {
            start = start.Date.AddHours(9);
        }

        DateBox.SelectedDate = start.Date;
        StartBox.Text = start.ToString("HH:mm");
        EndBox.Text = start.AddHours(1).ToString("HH:mm");

        ReminderBox.SelectedIndex = _settings.Current.SmartReminders
            ? 0
            : IndexOfReminderTag(_settings.Current.DefaultReminderMinutes);

        RepeatBox.SelectedIndex = 0;
    }

    private void LoadExisting(CalendarEvent item)
    {
        HeaderText.Text = "编辑日程";
        DeleteButton.Visibility = Visibility.Visible;

        TitleBox.Text = item.Title;
        DateBox.SelectedDate = item.Start.Date;
        AllDayBox.IsChecked = item.AllDay;
        StartBox.Text = item.Start.ToString("HH:mm");
        EndBox.Text = item.End.ToString("HH:mm");
        LocationBox.Text = item.Location ?? string.Empty;
        NotesBox.Text = item.Notes ?? string.Empty;

        var offset = item.Reminders
            .Select(r => (int)Math.Round((ReminderPlanner.FireAt(item.Start, item.AllDay, 0) - r.FireAt).TotalMinutes))
            .Where(x => x >= 0)
            .DefaultIfEmpty(-1)
            .Min();

        ReminderBox.SelectedIndex = offset < 0 ? IndexOfReminderTag(-1) : IndexOfReminderTag(offset);
        if (ReminderBox.SelectedIndex < 0)
        {
            ReminderBox.SelectedIndex = 0;
        }

        RepeatBox.SelectedIndex = RepeatIndexOf(item.RecurrenceRule);

        if (!string.IsNullOrWhiteSpace(item.RecurrenceRule))
        {
            ShowHint("这是重复日程，保存的修改会应用到整个系列。", isWarning: false);
        }

        AllDay_Changed(null, new RoutedEventArgs());
    }

    private void AllDay_Changed(object? sender, RoutedEventArgs e)
    {
        if (TimeRow is null)
        {
            return;
        }

        TimeRow.Visibility = AllDayBox.IsChecked == true ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var title = TitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            ShowHint("请先填写标题。", isWarning: true);
            TitleBox.Focus();
            return;
        }

        var date = (DateBox.SelectedDate ?? DateTime.Today).Date;
        var allDay = AllDayBox.IsChecked == true;
        DateTime start;
        DateTime end;

        if (allDay)
        {
            start = date;
            end = date.AddDays(1);
        }
        else
        {
            if (!TryParseTime(StartBox.Text, out var startTime))
            {
                ShowHint("开始时间格式应为 HH:mm，例如 09:30。", isWarning: true);
                return;
            }

            if (!TryParseTime(EndBox.Text, out var endTime))
            {
                ShowHint("结束时间格式应为 HH:mm，例如 10:30。", isWarning: true);
                return;
            }

            start = date.Add(startTime.ToTimeSpan());
            end = date.Add(endTime.ToTimeSpan());
            if (end <= start)
            {
                end = start.AddHours(1);
            }
        }

        if (!allDay)
        {
            var conflicts = ConflictDetector.FindConflicts(start, end, _store.GetAll(), _existing?.Id ?? 0);
            if (conflicts.Count > 0)
            {
                var names = string.Join("、", conflicts
                    .Select(c => c.Source.Title)
                    .Distinct()
                    .Take(3));
                var answer = MessageBox.Show(
                    this,
                    $"与「{names}」时间重叠，仍然保存吗？",
                    "时间冲突",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes)
                {
                    return;
                }
            }
        }

        var calendarEvent = new CalendarEvent
        {
            Id = _existing?.Id ?? 0,
            Title = title,
            Start = start,
            End = end,
            AllDay = allDay,
            Location = string.IsNullOrWhiteSpace(LocationBox.Text) ? null : LocationBox.Text.Trim(),
            Notes = string.IsNullOrWhiteSpace(NotesBox.Text) ? null : NotesBox.Text.Trim(),
            Source = _existing?.Source ?? EventSource.Manual,
            RawText = _existing?.RawText,
            Confidence = _existing?.Confidence ?? 1d,
            Status = _existing?.Status ?? EventStatus.Active,
            RecurrenceRule = BuildRecurrenceRule(date)
        };

        _store.Save(calendarEvent, ComputeReminderOffsets(start, allDay, title));
        Saved = true;
        DialogResult = true;
    }

    private IReadOnlyList<int> ComputeReminderOffsets(DateTime start, bool allDay, string title)
    {
        var tag = (ReminderBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        if (!int.TryParse(tag, out var value))
        {
            value = -2;
        }

        if (value == -1)
        {
            return Array.Empty<int>();
        }

        if (value != -2)
        {
            return new[] { value };
        }

        var draft = new EventDraft
        {
            Title = title,
            Start = start,
            End = allDay ? start.AddDays(1) : start.AddHours(1),
            AllDay = allDay,
            Notes = NotesBox.Text,
            Location = LocationBox.Text
        };
        return ReminderPlanner.Suggest(draft, _settings.Current.SmartReminders, _settings.Current.DefaultReminderMinutes);
    }

    private string? BuildRecurrenceRule(DateTime date)
    {
        var index = RepeatBox.SelectedIndex;
        if (index <= 0)
        {
            return null;
        }

        var iso = RecurrenceRule.IsoDay(date.DayOfWeek);
        return index switch
        {
            1 => "FREQ=DAILY;INTERVAL=1",
            2 => $"FREQ=WEEKLY;INTERVAL=1;BYDAY={RecurrenceRule.CodeFromIso(iso)}",
            3 => "FREQ=MONTHLY;INTERVAL=1",
            _ => "FREQ=YEARLY;INTERVAL=1"
        };
    }

    private static int RepeatIndexOf(string? rule)
    {
        if (string.IsNullOrWhiteSpace(rule))
        {
            return 0;
        }

        if (rule.Contains("FREQ=DAILY", StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (rule.Contains("FREQ=WEEKLY", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        if (rule.Contains("FREQ=MONTHLY", StringComparison.OrdinalIgnoreCase))
        {
            return 3;
        }

        return 4;
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (_existing is null)
        {
            return;
        }

        var answer = MessageBox.Show(this, "确定删除这条日程吗？", "删除", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        _store.Delete(_existing.Id);
        Saved = true;
        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void ShowHint(string text, bool isWarning)
    {
        HintText.Text = text;
        HintText.Foreground = isWarning
            ? (System.Windows.Media.Brush)FindResource("DangerBrush")
            : (System.Windows.Media.Brush)FindResource("MutedBrush");
        HintText.Visibility = Visibility.Visible;
    }

    private static bool TryParseTime(string text, out TimeOnly time)
        => TimeOnly.TryParseExact(text.Trim(), new[] { "HH:mm", "H:mm", "HH:mm:ss" },
            CultureInfo.InvariantCulture, DateTimeStyles.None, out time);

    private int IndexOfReminderTag(int minutes)
    {
        for (var i = 0; i < ReminderBox.Items.Count; i++)
        {
            if (ReminderBox.Items[i] is ComboBoxItem item &&
                int.TryParse(item.Tag?.ToString(), out var tag) &&
                tag == minutes)
            {
                return i;
            }
        }

        return -1;
    }

    private static DateTime DefaultStart()
    {
        var next = DateTime.Now.AddHours(1);
        return new DateTime(next.Year, next.Month, next.Day, next.Hour, 0, 0);
    }
}
