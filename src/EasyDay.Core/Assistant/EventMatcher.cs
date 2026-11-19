using System.Text;
using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.Core.Assistant;

/// <summary>匹配结果：命中的日程、候选列表、是否还需要用户点选。</summary>
public sealed record EventMatch(
    CalendarEvent? Best,
    EventOccurrence? BestOccurrence,
    IReadOnlyList<EventOccurrence> Candidates,
    bool Ambiguous);

/// <summary>
/// 把「周三的会」「项目周会」这类模糊指代映射到具体日程。
/// 打分：标题相似优先，日期用来排除干扰；分不清时给候选而不是瞎猜。
/// </summary>
public static class EventMatcher
{
    public static EventMatch Match(
        string? titleHint,
        DateOnly? dayHint,
        IEnumerable<CalendarEvent> events,
        DateTime now,
        int? preferEventId = null)
    {
        var active = events.Where(e => e.Status == EventStatus.Active).ToList();
        var occurrences = RecurrenceExpander
            .ForRange(active, now.Date.AddDays(-1), now.Date.AddDays(60))
            .Where(o => o.Source.Status == EventStatus.Active)
            .ToList();

        if (preferEventId is int preferred)
        {
            var forced = occurrences.FirstOrDefault(o => o.Source.Id == preferred);
            if (forced is not null)
            {
                return new EventMatch(forced.Source, forced, new[] { forced }, false);
            }
        }

        if (occurrences.Count == 0)
        {
            return new EventMatch(null, null, Array.Empty<EventOccurrence>(), false);
        }

        var hint = Normalize(titleHint);
        var scored = occurrences
            .Select(o => (Occurrence: o, Score: Score(o, hint, dayHint)))
            .ToList();

        var ranked = scored
            .GroupBy(x => x.Occurrence.Source.Id)
            .Select(g => g.OrderByDescending(x => x.Score).ThenBy(x => x.Occurrence.Start).First())
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Occurrence.Start)
            .ToList();

        var best = ranked[0];
        var candidates = ranked
            .Where(x => x.Score >= 2)
            .Take(3)
            .Select(x => x.Occurrence)
            .ToList();

        // 分不清的情况：没有有效信号、分数太低、或同分
        var tie = ranked.Count(x => x.Score == best.Score) > 1;
        var ambiguous = best.Score < 4 || tie;

        if (ambiguous)
        {
            return new EventMatch(
                null,
                null,
                candidates.Count > 0 ? candidates : ranked.Take(3).Select(x => x.Occurrence).ToList(),
                true);
        }

        return new EventMatch(best.Occurrence.Source, best.Occurrence, candidates, false);
    }

    private static int Score(EventOccurrence occurrence, string hint, DateOnly? dayHint)
    {
        var score = 0;

        if (dayHint is DateOnly day)
        {
            score += DateOnly.FromDateTime(occurrence.Start) == day ? 4 : -2;
        }

        if (hint.Length >= 2)
        {
            var title = Normalize(occurrence.Source.Title);
            if (title.Length >= 2)
            {
                if (title == hint)
                {
                    score += 10;
                }
                else if (title.Contains(hint, StringComparison.Ordinal) ||
                         hint.Contains(title, StringComparison.Ordinal))
                {
                    score += 6;
                }
                else if (BigramSimilarity(title, hint) >= 0.3)
                {
                    score += 3;
                }
            }
        }

        return score;
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || char.IsPunctuation(ch) || char.IsSymbol(ch))
            {
                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }

    /// <summary>二元组 Jaccard 相似度，用来容忍「和客户的会」这种说法。</summary>
    private static double BigramSimilarity(string left, string right)
    {
        if (left.Length < 2 || right.Length < 2)
        {
            return 0;
        }

        var a = Bigrams(left);
        var b = Bigrams(right);
        var intersection = a.Intersect(b).Count();
        var union = a.Union(b).Count();
        return union == 0 ? 0 : (double)intersection / union;
    }

    private static HashSet<string> Bigrams(string value)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i + 1 < value.Length; i++)
        {
            set.Add(value.Substring(i, 2));
        }

        return set;
    }
}
