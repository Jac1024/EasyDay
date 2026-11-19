using System.Globalization;

namespace EasyDay.Core.Calendar;

/// <summary>某一天的中式日历信息：农历、节气、节日、上班/休息。</summary>
public sealed record ChineseDayInfo(
    string LunarMonthDay,
    string LunarDay,
    string? Festival,
    string? SolarTerm,
    string? HolidayName,
    bool IsWorkday,
    bool IsStatutoryHoliday,
    bool IsMakeupWorkday,
    int WeekOfYear)
{
    /// <summary>格子右下角显示的小字：节日 &gt; 节气 &gt; 农历日。</summary>
    public string SubText => Festival ?? SolarTerm ?? LunarDay;

    /// <summary>节日/节气用不同颜色。</summary>
    public SubTextKind SubTextKind =>
        Festival is not null ? SubTextKind.Festival :
        SolarTerm is not null ? SubTextKind.SolarTerm :
        SubTextKind.Lunar;

    /// <summary>假期显示“休”，调休上班显示“班”。</summary>
    public string BadgeText => IsStatutoryHoliday ? "休" : IsMakeupWorkday ? "班" : string.Empty;

    public bool HasBadge => BadgeText.Length > 0;

    public string? HeaderExtra => Festival ?? SolarTerm ?? HolidayName;
}

public enum SubTextKind
{
    Lunar,
    Festival,
    SolarTerm
}

/// <summary>
/// 农历 / 节气 / 中国法定节假日。
/// 农历使用 .NET 内置 ChineseLunisolarCalendar（1901-2100）。
/// 节假日表目前内置 2026 年（国务院办公厅 2025-11-04 通知），其他年份回退为周末规则。
/// </summary>
public static class ChineseCalendarService
{
    private static readonly string[] MonthNames =
    {
        "正月", "二月", "三月", "四月", "五月", "六月",
        "七月", "八月", "九月", "十月", "冬月", "腊月"
    };

    private static readonly string[] Digits =
    {
        "", "一", "二", "三", "四", "五", "六", "七", "八", "九", "十"
    };

    // 2026 年放假安排（国办发明电〔2025〕7号）
    private static readonly (int Year, int Month, int Day)[] Holidays2026 =
    {
        // 元旦
        (2026, 1, 1), (2026, 1, 2), (2026, 1, 3),
        // 春节
        (2026, 2, 15), (2026, 2, 16), (2026, 2, 17), (2026, 2, 18), (2026, 2, 19),
        (2026, 2, 20), (2026, 2, 21), (2026, 2, 22), (2026, 2, 23),
        // 清明
        (2026, 4, 4), (2026, 4, 5), (2026, 4, 6),
        // 劳动节
        (2026, 5, 1), (2026, 5, 2), (2026, 5, 3), (2026, 5, 4), (2026, 5, 5),
        // 端午
        (2026, 6, 19), (2026, 6, 20), (2026, 6, 21),
        // 中秋
        (2026, 9, 25), (2026, 9, 26), (2026, 9, 27),
        // 国庆
        (2026, 10, 1), (2026, 10, 2), (2026, 10, 3), (2026, 10, 4),
        (2026, 10, 5), (2026, 10, 6), (2026, 10, 7)
    };

    // 2026 年调休上班日
    private static readonly (int Year, int Month, int Day)[] MakeupWorkdays2026 =
    {
        (2026, 1, 4), (2026, 2, 14), (2026, 2, 28), (2026, 5, 9), (2026, 9, 20), (2026, 10, 10)
    };

    private static readonly string[] HolidayNames2026 =
    {
        "元旦", "春节", "清明", "劳动节", "端午", "中秋", "国庆"
    };

    // 节气：名称、21 世纪 C 值、所在月份
    private static readonly (string Name, double C, int Month)[] SolarTerms =
    {
        ("小寒", 5.4055, 1), ("大寒", 20.12, 1),
        ("立春", 3.87, 2), ("雨水", 18.73, 2),
        ("惊蛰", 5.63, 3), ("春分", 20.646, 3),
        ("清明", 4.81, 4), ("谷雨", 20.1, 4),
        ("立夏", 5.52, 5), ("小满", 21.04, 5),
        ("芒种", 5.678, 6), ("夏至", 21.37, 6),
        ("小暑", 7.108, 7), ("大暑", 22.83, 7),
        ("立秋", 7.5, 8), ("处暑", 23.13, 8),
        ("白露", 7.646, 9), ("秋分", 23.042, 9),
        ("寒露", 8.318, 10), ("霜降", 23.438, 10),
        ("立冬", 7.438, 11), ("小雪", 22.36, 11),
        ("大雪", 7.18, 12), ("冬至", 21.94, 12)
    };

    /// <summary>按农历日期取信息；超出 1901-2100 时退化为公历信息。</summary>
    public static ChineseDayInfo Get(DateOnly date)
    {
        var festival = (string?)null;
        var lunarMonthDay = string.Empty;
        var lunarDay = string.Empty;
        var isLeap = false;
        var lunarMonth = 0;
        var lunarDayNumber = 0;

        try
        {
            var (year, month, day, leap) = ToLunar(date);
            lunarMonth = month;
            lunarDayNumber = day;
            isLeap = leap;
            lunarDay = DayName(day);
            lunarMonthDay = (leap ? "闰" : string.Empty) + MonthNames[month - 1] + lunarDay;

            if (!leap)
            {
                festival = LunarFestival(month, day);
            }

            // 除夕：明天是正月初一
            var next = date.AddDays(1);
            if (next.Year <= 2100)
            {
                var (_, nextMonth, nextDay, nextLeap) = ToLunar(next);
                if (!nextLeap && nextMonth == 1 && nextDay == 1)
                {
                    festival = "除夕";
                }
            }
        }
        catch (ArgumentOutOfRangeException)
        {
            lunarMonthDay = date.ToString("M月d日");
            lunarDay = string.Empty;
        }

        festival ??= SolarFestival(date);

        var solarTerm = GetSolarTerm(date);
        var (isHoliday, holidayName) = GetHoliday(date);
        var isMakeup = IsMakeupWorkday(date);

        var isWeekend = date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
        var isWorkday = isMakeup || (!isHoliday && !isWeekend);

        return new ChineseDayInfo(
            lunarMonthDay,
            lunarDay,
            festival,
            solarTerm,
            holidayName,
            isWorkday,
            isHoliday,
            isMakeup,
            ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue)));
    }

    private static (int Year, int Month, int Day, bool IsLeap) ToLunar(DateOnly date)
    {
        var calendar = new ChineseLunisolarCalendar();
        var time = date.ToDateTime(TimeOnly.MinValue);
        var year = calendar.GetYear(time);
        var rawMonth = calendar.GetMonth(time);
        var day = calendar.GetDayOfMonth(time);
        var leapMonth = calendar.GetLeapMonth(year);

        var isLeap = leapMonth > 0 && rawMonth == leapMonth;
        var month = leapMonth > 0 && rawMonth >= leapMonth ? rawMonth - 1 : rawMonth;
        return (year, month, day, isLeap);
    }

    private static string DayName(int day) => day switch
    {
        <= 0 => string.Empty,
        <= 10 => "初" + Digits[day],
        20 => "二十",
        < 20 => "十" + Digits[day - 10],
        < 30 => "廿" + Digits[day - 20],
        _ => "三十"
    };

    private static string? LunarFestival(int month, int day) => (month, day) switch
    {
        (1, 1) => "春节",
        (1, 15) => "元宵",
        (2, 2) => "龙抬头",
        (5, 5) => "端午",
        (7, 7) => "七夕",
        (7, 15) => "中元",
        (8, 15) => "中秋",
        (9, 9) => "重阳",
        (12, 8) => "腊八",
        (12, 23) => "小年",
        _ => null
    };

    private static string? SolarFestival(DateOnly date) => (date.Month, date.Day) switch
    {
        (1, 1) => "元旦",
        (2, 14) => "情人节",
        (3, 8) => "妇女节",
        (5, 1) => "劳动节",
        (5, 4) => "青年节",
        (6, 1) => "儿童节",
        (7, 1) => "建党节",
        (8, 1) => "建军节",
        (9, 10) => "教师节",
        (10, 1) => "国庆节",
        (12, 25) => "圣诞节",
        _ => null
    };

    private static string? GetSolarTerm(DateOnly date)
    {
        var year = date.Year;
        if (year is < 2000 or > 2099)
        {
            return null;
        }

        var y = year % 100;
        foreach (var (name, c, month) in SolarTerms)
        {
            if (month != date.Month)
            {
                continue;
            }

            var day = (int)(y * 0.2422 + c) - (y - 1) / 4;
            if (day == date.Day)
            {
                return name;
            }
        }

        return null;
    }

    private static (bool IsHoliday, string? Name) GetHoliday(DateOnly date)
    {
        if (date.Year != 2026)
        {
            return (false, null);
        }

        if (Holidays2026.Any(d => d.Month == date.Month && d.Day == date.Day))
        {
            return (true, HolidayNameFor(date));
        }

        return (false, null);
    }

    private static string? HolidayNameFor(DateOnly date)
    {
        if (date.Year != 2026)
        {
            return null;
        }

        return date.Month switch
        {
            1 => HolidayNames2026[0],
            2 => HolidayNames2026[1],
            4 => HolidayNames2026[2],
            5 => HolidayNames2026[3],
            6 => HolidayNames2026[4],
            9 => HolidayNames2026[5],
            10 => HolidayNames2026[6],
            _ => null
        };
    }

    private static bool IsMakeupWorkday(DateOnly date)
        => date.Year == 2026 && MakeupWorkdays2026.Any(d => d.Month == date.Month && d.Day == date.Day);
}
