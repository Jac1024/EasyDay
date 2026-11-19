using EasyDay.Core.Models;

namespace EasyDay.Core.Scheduling;

public static class ConflictDetector
{
    /// <summary>找出与给定时间段冲突的已有日程（会自动展开重复日程）。</summary>
    public static List<EventOccurrence> FindConflicts(
        DateTime start,
        DateTime end,
        IEnumerable<CalendarEvent> existing,
        int ignoreEventId = 0)
    {
        if (end <= start)
        {
            return new List<EventOccurrence>();
        }

        return existing
            .Where(e => e.Status == EventStatus.Active && !e.AllDay && e.Id != ignoreEventId)
            .SelectMany(e => RecurrenceExpander.Expand(e, start.AddDays(-1), end.AddDays(1)))
            .Where(o => o.Source.Status == EventStatus.Active)
            .Where(o => !o.Source.AllDay)
            .Where(o => o.Start < end && o.End > start)
            .OrderBy(o => o.Start)
            .ToList();
    }

    public static bool HasConflict(
        DateTime start,
        DateTime end,
        IEnumerable<CalendarEvent> existing,
        int ignoreEventId = 0)
        => FindConflicts(start, end, existing, ignoreEventId).Count > 0;
}
