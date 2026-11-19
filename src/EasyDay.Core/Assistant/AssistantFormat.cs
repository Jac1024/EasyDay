namespace EasyDay.Core.Assistant;

/// <summary>助手输出里的日期/时长格式，UI 和文案共用。</summary>
public static class AssistantFormat
{
    public static string DayLabel(DateTime day, DateTime now)
    {
        var delta = (day.Date - now.Date).Days;
        return delta switch
        {
            0 => "今天",
            1 => "明天",
            2 => "后天",
            -1 => "昨天",
            _ => $"{WeekdayName(day.DayOfWeek)} {day:MM-dd}"
        };
    }

    public static string Range(DateTime start, DateTime end) => $"{start:MM-dd} ~ {end:MM-dd}";

    public static string Span(DateTime start, DateTime end, DateTime now)
        => $"{DayLabel(start, now)} {start:HH:mm}–{end:HH:mm}";

    /// <summary>「周四 11-19 15:00–16:00」这种完整写法。</summary>
    public static string When(DateTime start, DateTime end, DateTime now)
        => start.Date == end.Date
            ? $"{DayLabel(start, now)} {start:HH:mm}–{end:HH:mm}"
            : $"{DayLabel(start, now)} {start:HH:mm} – {DayLabel(end, now)} {end:HH:mm}";

    public static string Clock(DateTime start, DateTime end)
        => start.Date == end.Date
            ? $"{start:MM-dd HH:mm}–{end:HH:mm}"
            : $"{start:MM-dd HH:mm}–{end:MM-dd HH:mm}";

    public static string Duration(TimeSpan span)
    {
        var hours = span.TotalHours;
        if (hours >= 1)
        {
            return Math.Abs(hours - Math.Round(hours)) < 0.05
                ? $"{Math.Round(hours):0} 小时"
                : $"{hours:0.#} 小时";
        }

        return $"{(int)Math.Round(span.TotalMinutes)} 分钟";
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
