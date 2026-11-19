using EasyDay.Core.Models;

namespace EasyDay.Core.Scheduling;

public sealed record DuplicateMatch(CalendarEvent Existing, DateTime Start);

/// <summary>判断草稿是否与已有日程重复：同一天、时间相差 ±5 分钟、标题相似。</summary>
public static class DuplicateDetector
{
    public static DuplicateMatch? Find(EventDraft draft, IEnumerable<CalendarEvent> existing)
    {
        var dayStart = draft.Start.Date;
        var dayEnd = dayStart.AddDays(1);

        foreach (var item in existing)
        {
            if (item.Status == EventStatus.Cancelled || item.AllDay != draft.AllDay)
            {
                continue;
            }

            foreach (var occurrence in RecurrenceExpander.Expand(item, dayStart, dayEnd))
            {
                if (occurrence.Start.Date != draft.Start.Date)
                {
                    continue;
                }

                if (Math.Abs((occurrence.Start - draft.Start).TotalMinutes) > 5)
                {
                    continue;
                }

                if (Similar(draft.Title, item.Title))
                {
                    return new DuplicateMatch(item, occurrence.Start);
                }
            }
        }

        return null;
    }

    public static bool Similar(string? left, string? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (a == b)
        {
            return true;
        }

        if (a.Contains(b, StringComparison.Ordinal) || b.Contains(a, StringComparison.Ordinal))
        {
            return Math.Min(a.Length, b.Length) >= 2;
        }

        var distance = Levenshtein(a, b);
        var ratio = 1d - (double)distance / Math.Max(a.Length, b.Length);
        return ratio >= 0.8;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .Where(c => !char.IsWhiteSpace(c) && !char.IsPunctuation(c) && !char.IsSymbol(c))
            .ToArray();

        return new string(chars).ToLowerInvariant();
    }

    private static int Levenshtein(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];

        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
