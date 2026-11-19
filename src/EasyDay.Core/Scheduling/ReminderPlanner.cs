using EasyDay.Core.Models;

namespace EasyDay.Core.Scheduling;

public static class ReminderPlanner
{
    public const int MaxOffsetMinutes = 60 * 24 * 7;

    /// <summary>按事件类型给出提醒策略。</summary>
    public static IReadOnlyList<int> Suggest(EventDraft draft, bool smart = true, int fallbackMinutes = 15)
    {
        if (!smart)
        {
            return Sanitize(new[] { fallbackMinutes });
        }

        var text = $"{draft.Title} {draft.Notes} {draft.Location}";

        if (draft.AllDay && ContainsAny(text, "截止", "提交", "报名", "deadline", "之前", "前完成"))
        {
            return Sanitize(new[] { 0, 1440, 4320 });
        }

        if (ContainsAny(text, "截止", "提交", "报名", "deadline"))
        {
            return Sanitize(new[] { 60, 1440, 4320 });
        }

        if (ContainsAny(text, "机场", "高铁", "车站", "出发", "航班"))
        {
            return Sanitize(new[] { 120, 1440 });
        }

        if (draft.AllDay)
        {
            return Sanitize(new[] { 0, 1440 });
        }

        return Sanitize(new[] { 15, 1440 });
    }

    public static DateTime FireAt(DateTime start, bool allDay, int offsetMinutes)
    {
        var baseTime = allDay ? start.Date.AddHours(9) : start;
        return baseTime.AddMinutes(-offsetMinutes);
    }

    /// <summary>过滤非法值、去重、从近到远排序。</summary>
    public static IReadOnlyList<int> Sanitize(IEnumerable<int> offsets)
    {
        return offsets
            .Where(x => x >= 0 && x <= MaxOffsetMinutes)
            .Distinct()
            .OrderByDescending(x => x)
            .ToList();
    }

    public static List<Reminder> Build(DateTime start, bool allDay, IEnumerable<int> offsets, DateTime now)
    {
        var result = new List<Reminder>();
        foreach (var offset in Sanitize(offsets))
        {
            var fireAt = FireAt(start, allDay, offset);
            if (fireAt <= now)
            {
                continue;
            }

            result.Add(new Reminder { FireAt = fireAt });
        }

        return result;
    }

    private static bool ContainsAny(string text, params string[] keywords)
        => keywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
