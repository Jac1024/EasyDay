using System.Globalization;
using EasyDay.Core.Models;

namespace EasyDay.Core.Scheduling;

/// <summary>重复日程的一次实例（不落库，按需展开）。</summary>
public sealed record EventOccurrence(CalendarEvent Source, DateTime Start, DateTime End)
{
    public bool IsRecurringInstance => Source.IsRecurring;
}

/// <summary>RFC 5545 RRULE 子集：FREQ / INTERVAL / BYDAY / UNTIL / COUNT。</summary>
public sealed class RecurrenceRule
{
    public string Frequency { get; init; } = "DAILY";
    public int Interval { get; init; } = 1;
    public List<int> ByDay { get; init; } = new();
    public DateTime? Until { get; init; }
    public int? Count { get; init; }

    public static RecurrenceRule? Parse(string? rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule))
        {
            return null;
        }

        var rule = new RecurrenceRule();
        string freq = "DAILY";
        int interval = 1;
        var byDay = new List<int>();
        DateTime? until = null;
        int? count = null;

        foreach (var part in rrule.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length != 2)
            {
                continue;
            }

            var key = kv[0].ToUpperInvariant();
            var value = kv[1].Trim();
            switch (key)
            {
                case "FREQ":
                    freq = value.ToUpperInvariant();
                    break;
                case "INTERVAL":
                    if (int.TryParse(value, out var n) && n > 0)
                    {
                        interval = n;
                    }

                    break;
                case "BYDAY":
                    foreach (var d in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var iso = IsoFromCode(d.Trim().ToUpperInvariant());
                        if (iso > 0)
                        {
                            byDay.Add(iso);
                        }
                    }

                    break;
                case "UNTIL":
                    until = ParseUntil(value);
                    break;
                case "COUNT":
                    if (int.TryParse(value, out var c) && c > 0)
                    {
                        count = c;
                    }

                    break;
            }
        }

        return new RecurrenceRule
        {
            Frequency = freq,
            Interval = interval,
            ByDay = byDay,
            Until = until,
            Count = count
        };
    }

    /// <summary>展开 [rangeStart, rangeEnd) 内的所有开始时间。</summary>
    public IEnumerable<DateTime> Expand(DateTime firstStart, DateTime rangeStart, DateTime rangeEnd)
    {
        var time = firstStart.TimeOfDay;
        var guardEnd = rangeEnd.AddYears(1);
        var produced = 0;
        var interval = Math.Max(1, Interval);
        var days = ByDay.Count > 0 ? ByDay : new List<int> { IsoDay(firstStart.DayOfWeek) };

        for (var i = 0; i < 5000; i++)
        {
            List<DateTime> candidates;
            switch (Frequency)
            {
                case "DAILY":
                    candidates = new List<DateTime> { firstStart.Date.AddDays(i * interval).Add(time) };
                    break;

                case "WEEKLY":
                    {
                        var weekStart = StartOfWeek(firstStart.Date).AddDays(7 * interval * i);
                        candidates = days
                            .Select(d => weekStart.AddDays(d - 1).Add(time))
                            .OrderBy(x => x)
                            .ToList();
                        break;
                    }

                case "MONTHLY":
                    {
                        var monthStart = new DateTime(firstStart.Year, firstStart.Month, 1).AddMonths(i * interval);
                        var day = Math.Min(firstStart.Day, DateTime.DaysInMonth(monthStart.Year, monthStart.Month));
                        candidates = new List<DateTime> { new DateTime(monthStart.Year, monthStart.Month, day).Add(time) };
                        break;
                    }

                case "YEARLY":
                    {
                        var yearStart = new DateTime(firstStart.Year, firstStart.Month, 1).AddYears(i * interval);
                        var day = Math.Min(firstStart.Day, DateTime.DaysInMonth(yearStart.Year, yearStart.Month));
                        candidates = new List<DateTime> { new DateTime(yearStart.Year, yearStart.Month, day).Add(time) };
                        break;
                    }

                default:
                    yield break;
            }

            var anyInsideGuard = false;
            foreach (var candidate in candidates)
            {
                if (candidate > guardEnd)
                {
                    continue;
                }

                anyInsideGuard = true;

                if (candidate < firstStart)
                {
                    continue;
                }

                if (Until.HasValue && candidate > Until.Value)
                {
                    yield break;
                }

                if (Count.HasValue && produced >= Count.Value)
                {
                    yield break;
                }

                produced++;

                if (candidate >= rangeEnd)
                {
                    yield break;
                }

                if (candidate >= rangeStart)
                {
                    yield return candidate;
                }
            }

            if (!anyInsideGuard)
            {
                yield break;
            }
        }
    }

    private static DateTime? ParseUntil(string value)
    {
        value = value.Trim();

        // ICS 里的 UNTIL 是"包含该天"，所以 20261118 表示到 18 日 23:59:59 为止
        if (value.Length == 8 &&
            DateTime.TryParseExact(value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly))
        {
            return dateOnly.Date.AddDays(1).AddSeconds(-1);
        }

        string[] formats =
        {
            "yyyyMMdd'T'HHmmss'Z'",
            "yyyyMMdd'T'HHmmss",
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd"
        };

        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces, out var exact))
        {
            return exact;
        }

        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var loose)
            ? loose
            : null;
    }

    public static DateTime StartOfWeek(DateTime date)
    {
        var offset = ((int)date.DayOfWeek + 6) % 7; // Monday = 0
        return date.Date.AddDays(-offset);
    }

    public static int IsoDay(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => 1,
        DayOfWeek.Tuesday => 2,
        DayOfWeek.Wednesday => 3,
        DayOfWeek.Thursday => 4,
        DayOfWeek.Friday => 5,
        DayOfWeek.Saturday => 6,
        _ => 7
    };

    public static int IsoFromCode(string code) => code switch
    {
        "MO" => 1,
        "TU" => 2,
        "WE" => 3,
        "TH" => 4,
        "FR" => 5,
        "SA" => 6,
        "SU" => 7,
        _ => 0
    };

    public static string CodeFromIso(int iso) => iso switch
    {
        1 => "MO",
        2 => "TU",
        3 => "WE",
        4 => "TH",
        5 => "FR",
        6 => "SA",
        _ => "SU"
    };

    public string ToRruleString()
    {
        var parts = new List<string> { $"FREQ={Frequency}", $"INTERVAL={Math.Max(1, Interval)}" };
        if (ByDay.Count > 0)
        {
            parts.Add("BYDAY=" + string.Join(",", ByDay.OrderBy(x => x).Select(CodeFromIso)));
        }

        if (Until.HasValue)
        {
            parts.Add("UNTIL=" + Until.Value.ToString("yyyyMMdd"));
        }

        if (Count.HasValue)
        {
            parts.Add("COUNT=" + Count.Value);
        }

        return string.Join(";", parts);
    }
}

public static class RecurrenceExpander
{
    /// <summary>展开某条日程在 [rangeStart, rangeEnd) 内的实例。</summary>
    public static IEnumerable<EventOccurrence> Expand(CalendarEvent source, DateTime rangeStart, DateTime rangeEnd)
    {
        var duration = source.End - source.Start;
        if (duration <= TimeSpan.Zero)
        {
            duration = source.AllDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1);
        }

        var rule = RecurrenceRule.Parse(source.RecurrenceRule);
        if (rule is null)
        {
            var end = source.Start + duration;
            if (Overlaps(source.Start, end, rangeStart, rangeEnd))
            {
                yield return new EventOccurrence(source, source.Start, end);
            }

            yield break;
        }

        foreach (var start in rule.Expand(source.Start, rangeStart, rangeEnd))
        {
            var end = start + duration;
            if (Overlaps(start, end, rangeStart, rangeEnd))
            {
                yield return new EventOccurrence(source, start, end);
            }
        }
    }

    public static bool Overlaps(DateTime start, DateTime end, DateTime rangeStart, DateTime rangeEnd)
        => start < rangeEnd && end > rangeStart;

    public static List<EventOccurrence> ForDay(IEnumerable<CalendarEvent> events, DateOnly day)
    {
        var dayStart = day.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        return events
            .Where(x => x.Status != EventStatus.Cancelled)
            .SelectMany(x => Expand(x, dayStart, dayEnd))
            .OrderBy(x => x.Start)
            .ToList();
    }

    public static List<EventOccurrence> ForRange(IEnumerable<CalendarEvent> events, DateTime from, DateTime to)
    {
        return events
            .Where(x => x.Status != EventStatus.Cancelled)
            .SelectMany(x => Expand(x, from, to))
            .OrderBy(x => x.Start)
            .ToList();
    }
}
