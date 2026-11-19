using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using EasyDay.App.Infrastructure;
using EasyDay.Core.Calendar;
using EasyDay.Core.Data;
using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.App.ViewModels;

public sealed class WidgetViewModel : ObservableObject
{
    private readonly EventStore _store;
    private List<CalendarEvent> _all = new();
    private DateOnly _selectedDate;
    private DateOnly _month;
    private bool _isCollapsed;

    public WidgetViewModel(EventStore store)
    {
        _store = store;
        _selectedDate = DateOnly.FromDateTime(DateTime.Today);
        _month = new DateOnly(_selectedDate.Year, _selectedDate.Month, 1);

        SelectDayCommand = new RelayCommand(parameter =>
        {
            if (parameter is DayCellViewModel cell)
            {
                SelectDay(cell.Date);
            }
        });

        PrevMonthCommand = new RelayCommand(() => ChangeMonth(-1));
        NextMonthCommand = new RelayCommand(() => ChangeMonth(1));
        TodayCommand = new RelayCommand(GoToday);
        CreateEventCommand = new RelayCommand(parameter =>
            CreateRequested?.Invoke(parameter is DayCellViewModel cell ? cell.Date : _selectedDate));
        AiCaptureCommand = new RelayCommand(() => AiCaptureRequested?.Invoke());
        EditEventCommand = new RelayCommand(parameter =>
        {
            if (parameter is EventItemViewModel item)
            {
                EditRequested?.Invoke(item.Occurrence);
            }
        });
        SettingsCommand = new RelayCommand(() => SettingsRequested?.Invoke());
    }

    public ObservableCollection<DayCellViewModel> Days { get; } = new();

    public ObservableCollection<string> WeekNumbers { get; } = new();

    public ObservableCollection<EventItemViewModel> Agenda { get; } = new();

    public ICommand SelectDayCommand { get; }
    public ICommand PrevMonthCommand { get; }
    public ICommand NextMonthCommand { get; }
    public ICommand TodayCommand { get; }
    public ICommand CreateEventCommand { get; }
    public ICommand AiCaptureCommand { get; }
    public ICommand EditEventCommand { get; }
    public ICommand SettingsCommand { get; }

    public event Action<DateOnly?>? CreateRequested;

    public event Action? AiCaptureRequested;

    public event Action<EventOccurrence>? EditRequested;

    public event Action? SettingsRequested;

    public string HeaderDate => $"{_selectedDate.Month}月{_selectedDate.Day}日";
    public string HeaderWeekday => WeekdayName(_selectedDate.DayOfWeek);

    public string HeaderLunar
    {
        get
        {
            var info = ChineseCalendarService.Get(_selectedDate);
            var text = string.IsNullOrWhiteSpace(info.LunarMonthDay)
                ? string.Empty
                : "农历" + info.LunarMonthDay;

            var extra = info.Festival ?? info.SolarTerm ?? (info.HolidayName is null ? null : info.HolidayName + "假期");
            return string.IsNullOrWhiteSpace(text)
                ? extra ?? string.Empty
                : string.IsNullOrWhiteSpace(extra) ? text : $"{text} · {extra}";
        }
    }

    public string MonthTitle => $"{_month.Year} 年 {_month.Month} 月";

    /// <summary>收起月历，只保留标题、今日日程和底部按钮。</summary>
    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => SetProperty(ref _isCollapsed, value);
    }

    public string AgendaTitle
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            if (_selectedDate == today)
            {
                return "今天";
            }

            if (_selectedDate == today.AddDays(1))
            {
                return "明天";
            }

            return $"{_selectedDate.Month}月{_selectedDate.Day}日 {WeekdayName(_selectedDate.DayOfWeek)}";
        }
    }

    public string AgendaCountText => Agenda.Count == 0 ? string.Empty : $"{Agenda.Count} 项";

    public bool IsAgendaEmpty => Agenda.Count == 0;

    public void Refresh()
    {
        _all = _store.GetAll();
        BuildDays();
        BuildAgenda();
    }

    public void SelectDay(DateOnly date)
    {
        _selectedDate = date;
        _month = new DateOnly(date.Year, date.Month, 1);
        BuildDays();
        BuildAgenda();
        NotifyHeader();
    }

    public void GoToday()
    {
        SelectDay(DateOnly.FromDateTime(DateTime.Today));
    }

    private void ChangeMonth(int delta)
    {
        _month = _month.AddMonths(delta);
        var today = DateOnly.FromDateTime(DateTime.Today);
        _selectedDate = _month.Year == today.Year && _month.Month == today.Month ? today : _month;
        BuildDays();
        BuildAgenda();
        NotifyHeader();
    }

    private void BuildDays()
    {
        var first = new DateOnly(_month.Year, _month.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7; // 周一开头
        var gridStart = first.AddDays(-offset);
        var gridEnd = gridStart.AddDays(42);
        var today = DateOnly.FromDateTime(DateTime.Today);

        var counts = RecurrenceExpander
            .ForRange(_all, gridStart.ToDateTime(TimeOnly.MinValue), gridEnd.ToDateTime(TimeOnly.MinValue))
            .GroupBy(o => DateOnly.FromDateTime(o.Start))
            .ToDictionary(g => g.Key, g => g.Count());

        Days.Clear();
        for (var i = 0; i < 42; i++)
        {
            var date = gridStart.AddDays(i);
            Days.Add(new DayCellViewModel
            {
                Date = date,
                Info = ChineseCalendarService.Get(date),
                IsCurrentMonth = date.Month == _month.Month && date.Year == _month.Year,
                IsToday = date == today,
                IsSelected = date == _selectedDate,
                EventCount = counts.GetValueOrDefault(date)
            });
        }

        WeekNumbers.Clear();
        for (var week = 0; week < 6; week++)
        {
            var weekStart = gridStart.AddDays(week * 7);
            WeekNumbers.Add(ISOWeek.GetWeekOfYear(weekStart.ToDateTime(TimeOnly.MinValue)).ToString());
        }
    }

    private void BuildAgenda()
    {
        Agenda.Clear();
        foreach (var occurrence in RecurrenceExpander.ForDay(_all, _selectedDate))
        {
            Agenda.Add(new EventItemViewModel(occurrence));
        }

        OnPropertyChanged(nameof(IsAgendaEmpty));
        OnPropertyChanged(nameof(AgendaCountText));
    }

    private void NotifyHeader()
    {
        OnPropertyChanged(nameof(HeaderDate));
        OnPropertyChanged(nameof(HeaderWeekday));
        OnPropertyChanged(nameof(HeaderLunar));
        OnPropertyChanged(nameof(MonthTitle));
        OnPropertyChanged(nameof(AgendaTitle));
    }

    public static string WeekdayName(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "周一",
        DayOfWeek.Tuesday => "周二",
        DayOfWeek.Wednesday => "周三",
        DayOfWeek.Thursday => "周四",
        DayOfWeek.Friday => "周五",
        DayOfWeek.Saturday => "周六",
        _ => "周日"
    };
}
