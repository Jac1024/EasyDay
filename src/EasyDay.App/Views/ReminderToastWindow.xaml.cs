using System.Windows;
using System.Windows.Threading;
using EasyDay.Core.Models;

namespace EasyDay.App.Views;

public partial class ReminderToastWindow : Window
{
    private readonly Reminder _reminder;
    private readonly DispatcherTimer _autoClose;

    public ReminderToastWindow(Reminder reminder)
    {
        InitializeComponent();
        _reminder = reminder;

        var item = reminder.Event;
        TitleText.Text = item?.Title ?? "日程提醒";
        TimeText.Text = item is null
            ? reminder.FireAt.ToString("MM-dd HH:mm")
            : item.AllDay
                ? $"{item.Start:MM-dd} 全天"
                : $"{item.Start:MM-dd HH:mm}";

        if (item is not null && !string.IsNullOrWhiteSpace(item.Location))
        {
            LocationText.Text = item.Location;
            LocationText.Visibility = Visibility.Visible;
        }

        _autoClose = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _autoClose.Tick += (_, _) => Close();
        _autoClose.Start();
        Closed += (_, _) => _autoClose.Stop();
    }

    public event Action<Reminder>? CompleteRequested;

    public event Action<Reminder>? SnoozeRequested;

    public event Action<Reminder>? OpenRequested;

    public void SetPosition(double workRight, double workBottom, double verticalOffset)
    {
        Left = workRight - Width - 8;
        Top = workBottom - Height - 8 - verticalOffset;
    }

    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        CompleteRequested?.Invoke(_reminder);
        Close();
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        SnoozeRequested?.Invoke(_reminder);
        Close();
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        OpenRequested?.Invoke(_reminder);
        Close();
    }

    private void Dismiss_Click(object sender, RoutedEventArgs e) => Close();
}
