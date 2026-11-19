using EasyDay.App.Infrastructure;
using EasyDay.Core.Calendar;

namespace EasyDay.App.ViewModels;

public sealed class DayCellViewModel : ObservableObject
{
    private bool _isSelected;
    private bool _isToday;
    private bool _isCurrentMonth;
    private int _eventCount;

    public required DateOnly Date { get; init; }

    public required ChineseDayInfo Info { get; init; }

    public int DayNumber => Date.Day;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsToday
    {
        get => _isToday;
        set => SetProperty(ref _isToday, value);
    }

    public bool IsCurrentMonth
    {
        get => _isCurrentMonth;
        set => SetProperty(ref _isCurrentMonth, value);
    }

    public int EventCount
    {
        get => _eventCount;
        set
        {
            if (SetProperty(ref _eventCount, value))
            {
                OnPropertyChanged(nameof(DotsText));
            }
        }
    }

    // ---- 中式日历 ----

    public bool IsNonWorkday => !Info.IsWorkday;

    public string SubText => Info.SubText;

    public SubTextKind SubTextKind => Info.SubTextKind;

    public string BadgeText => Info.BadgeText;

    public bool HasBadge => Info.HasBadge;

    public bool IsMakeupWorkday => Info.IsMakeupWorkday;

    public string DotsText => _eventCount > 0 ? "•" : string.Empty;

    public string ToolTipText
    {
        get
        {
            var parts = new List<string> { $"{Date:M月d日}" };
            if (!string.IsNullOrWhiteSpace(Info.LunarMonthDay))
            {
                parts.Add("农历" + Info.LunarMonthDay);
            }

            if (Info.Festival is not null)
            {
                parts.Add(Info.Festival);
            }

            if (Info.SolarTerm is not null)
            {
                parts.Add(Info.SolarTerm);
            }

            if (Info.HolidayName is not null)
            {
                parts.Add(Info.HolidayName + "假期");
            }
            else if (Info.IsMakeupWorkday)
            {
                parts.Add("调休上班");
            }

            return string.Join(" · ", parts);
        }
    }
}
