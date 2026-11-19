using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.Core.Assistant;

/// <summary>自然语言助手的意图。</summary>
public enum AssistantIntent
{
    /// <summary>不像命令，按新建日程处理。</summary>
    Create,

    /// <summary>查询空闲 / 安排。</summary>
    Query,

    /// <summary>改期。</summary>
    Reschedule,

    /// <summary>取消。</summary>
    Cancel,

    Unknown
}

/// <summary>一段空闲时间。</summary>
public sealed record FreeSlot(DateTime Start, DateTime End)
{
    public double Hours => (End - Start).TotalHours;
}

/// <summary>查询窗口：日期范围 + 每天的工作时间。</summary>
public sealed record TimeWindow(
    DateTime Start,
    DateTime End,
    TimeSpan DayStart,
    TimeSpan DayEnd,
    string Label,
    bool IncludeWeekends);

/// <summary>从「把 X 改到 Y」里解析出的改期请求。</summary>
public sealed record MoveRequest(
    string? TitleHint,
    DateOnly? DayHint,
    DateOnly? NewDate,
    TimeOnly? NewTime,
    string? Period,
    int ShiftMinutes)
{
    public bool HasNewTime => NewDate.HasValue || NewTime.HasValue || ShiftMinutes != 0;

    /// <summary>结合原日程算出新的开始时间；算不出返回 null。</summary>
    public DateTime? ResolveStart(DateTime original)
    {
        if (ShiftMinutes != 0)
        {
            return original.AddMinutes(ShiftMinutes);
        }

        if (NewDate is null && NewTime is null)
        {
            return null;
        }

        var date = NewDate?.ToDateTime(TimeOnly.MinValue) ?? original.Date;
        var time = NewTime;

        if (time is null)
        {
            var originalTime = TimeOnly.FromTimeSpan(original.TimeOfDay);
            time = Period is null || InPeriod(originalTime, Period)
                ? originalTime
                : DefaultHour(Period);
        }

        return date.Date + time.Value.ToTimeSpan();
    }

    private static bool InPeriod(TimeOnly time, string period) => period switch
    {
        "上午" => time.Hour is >= 5 and < 12,
        "中午" => time.Hour is >= 11 and < 14,
        "下午" => time.Hour is >= 12 and < 18,
        "晚上" => time.Hour is >= 18 and < 24,
        _ => true
    };

    private static TimeOnly DefaultHour(string period) => period switch
    {
        "上午" => new TimeOnly(9, 0),
        "中午" => new TimeOnly(12, 0),
        "下午" => new TimeOnly(15, 0),
        "晚上" => new TimeOnly(19, 0),
        _ => new TimeOnly(9, 0)
    };
}

/// <summary>助手对一句话的处理结果。</summary>
public sealed record AssistantResult
{
    public AssistantIntent Intent { get; init; } = AssistantIntent.Unknown;

    /// <summary>给用户看的自然语言回答。</summary>
    public string Message { get; init; } = string.Empty;

    /// <summary>查询到的空闲段。</summary>
    public IReadOnlyList<FreeSlot> FreeSlots { get; init; } = Array.Empty<FreeSlot>();

    /// <summary>相关的已有日程（查询结果或候选）。</summary>
    public IReadOnlyList<EventOccurrence> Related { get; init; } = Array.Empty<EventOccurrence>();

    /// <summary>改期/取消命中的日程。</summary>
    public CalendarEvent? Target { get; init; }

    public DateTime? NewStart { get; init; }

    public DateTime? NewEnd { get; init; }

    /// <summary>改期后与其他日程的重叠。</summary>
    public IReadOnlyList<EventOccurrence> Conflicts { get; init; } = Array.Empty<EventOccurrence>();

    /// <summary>需要用户先点选具体日程。</summary>
    public bool NeedsClarification { get; init; }

    public string? Error { get; init; }

    public bool CanApply =>
        !NeedsClarification &&
        Error is null &&
        Target is not null &&
        (Intent == AssistantIntent.Cancel || (NewStart is not null && NewEnd is not null));
}
