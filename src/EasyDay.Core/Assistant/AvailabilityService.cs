using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.Core.Assistant;

/// <summary>按日期范围和每天的工作时段计算空闲，并生成中文说明。</summary>
public static class AvailabilityService
{
    public const int DefaultMinMinutes = 30;

    /// <summary>
    /// 找出窗口内的空闲段。规则：
    /// - 只看 Active 日程；重复日程会展开
    /// - 全天日程占满当天
    /// - 周末默认跳过（查询里明确提到周末时才看）
    /// - 已经过去的时间不返回
    /// </summary>
    public static List<FreeSlot> FindFreeSlots(
        IEnumerable<CalendarEvent> events,
        TimeWindow window,
        DateTime now,
        int minMinutes = DefaultMinMinutes)
    {
        var result = new List<FreeSlot>();
        var active = events.Where(e => e.Status == EventStatus.Active).ToList();
        if (active.Count == 0 && window.End <= now)
        {
            return result;
        }

        var occurrences = RecurrenceExpander
            .ForRange(active, window.Start.AddDays(-1), window.End.AddDays(1))
            .ToList();

        var from = window.Start < now ? now : window.Start;
        if (from >= window.End)
        {
            return result;
        }

        // 从当前分钟开始，避免把 15:07 这种半分钟空档算进来
        from = new DateTime(from.Year, from.Month, from.Day, from.Hour, from.Minute, 0);

        for (var day = from.Date; day < window.End.Date; day = day.AddDays(1))
        {
            if (!window.IncludeWeekends && day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var dayStart = day + window.DayStart;
            var dayEnd = day + window.DayEnd;
            var start = dayStart < from ? from : dayStart;
            var end = dayEnd > window.End ? window.End : dayEnd;

            if (end <= start)
            {
                continue;
            }

            var allDay = occurrences.Any(o =>
                o.Source.AllDay && o.Start.Date <= day && o.End.Date > day);
            if (allDay)
            {
                continue;
            }

            var busy = occurrences
                .Where(o => !o.Source.AllDay && o.Start < end && o.End > start)
                .Select(o => (
                    Start: o.Start < start ? start : o.Start,
                    End: o.End > end ? end : o.End))
                .OrderBy(x => x.Start)
                .ToList();

            var cursor = start;
            foreach (var block in busy)
            {
                if (block.Start > cursor && (block.Start - cursor).TotalMinutes >= minMinutes)
                {
                    result.Add(new FreeSlot(cursor, block.Start));
                }

                if (block.End > cursor)
                {
                    cursor = block.End;
                }
            }

            if (end > cursor && (end - cursor).TotalMinutes >= minMinutes)
            {
                result.Add(new FreeSlot(cursor, end));
            }
        }

        return result;
    }

    public static string Summarize(
        IReadOnlyList<FreeSlot> slots,
        IEnumerable<EventOccurrence> related,
        TimeWindow window,
        DateTime now,
        int minMinutes = DefaultMinMinutes,
        int maxSlots = 4)
    {
        var hours = $"{window.DayStart:hh\\:mm}–{window.DayEnd:hh\\:mm}";
        var relatedList = related
            .Where(o => o.Source.Status == EventStatus.Active && !o.Source.AllDay)
            .OrderBy(o => o.Start)
            .ToList();

        var builder = new System.Text.StringBuilder();
        if (slots.Count == 0)
        {
            builder.Append($"「{window.Label}」{hours} 没有 {minMinutes} 分钟以上的空档。");
        }
        else
        {
            builder.Append($"「{window.Label}」{hours} 还有 {slots.Count} 段空档：");
            foreach (var slot in slots.Take(maxSlots))
            {
                builder.AppendLine();
                builder.Append($"· {AssistantFormat.Span(slot.Start, slot.End, now)}");
                builder.Append($"（{AssistantFormat.Duration(slot.End - slot.Start)}）");
            }

            if (slots.Count > maxSlots)
            {
                builder.AppendLine();
                builder.Append($"…另有 {slots.Count - maxSlots} 段");
            }
        }

        if (relatedList.Count > 0)
        {
            builder.AppendLine();
            builder.Append("已排：");
            builder.Append(string.Join("、", relatedList.Take(5).Select(o =>
                $"{AssistantFormat.DayLabel(o.Start, now)} {o.Start:HH:mm} {o.Source.Title}")));
        }

        return builder.ToString();
    }
}
