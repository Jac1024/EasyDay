using System.Globalization;
using System.Text.RegularExpressions;
using EasyDay.Core.Parsing;

namespace EasyDay.Core.Assistant;

/// <summary>
/// 把一句话拆成意图、查询窗口或改期请求。纯规则实现：
/// 查询和改期必须稳定，宁可追问也不要猜错。
/// </summary>
public static class AssistantText
{
    private static readonly string[] CancelKeywords = { "取消", "不去了", "删掉", "删除" };

    private static readonly (string Word, bool Earlier)[] MoveKeywords =
    {
        ("改期", false), ("改到", false), ("改在", false), ("改去", false),
        ("挪到", false), ("挪去", false), ("移到", false), ("移去", false),
        ("换到", false), ("调到", false), ("调整到", false), ("安排到", false),
        ("改一下", false), ("改下", false), ("改个时间", false), ("换个时间", false),
        ("推后", false), ("推迟", false), ("延后", false), ("延到", false), ("往后推", false),
        ("提前", true), ("提早", true)
    };

    private static readonly string[] QueryKeywords =
    {
        "有空", "空闲", "空档", "没事", "忙吗", "忙不忙", "还有", "安排得下",
        "冲突", "怎么样", "吗", "呢", "？", "?"
    };

    private static readonly Regex WeekdayRegex = new(
        @"(?<pre>下下周|下下个星期|下个?星期|下个?周|下周|这个?星期|这个?周|这周|本周|星期|礼拜|周)(?<w>[一二三四五六日天])",
        RegexOptions.Compiled);

    public static AssistantIntent DetectIntent(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return AssistantIntent.Unknown;
        }

        if (CancelKeywords.Any(text.Contains))
        {
            return AssistantIntent.Cancel;
        }

        if (MoveKeywords.Any(k => text.Contains(k.Word)))
        {
            return AssistantIntent.Reschedule;
        }

        if (QueryKeywords.Any(text.Contains))
        {
            return AssistantIntent.Query;
        }

        return AssistantIntent.Create;
    }

    /// <summary>解析查询的日期范围和工作时段。</summary>
    public static TimeWindow ParseWindow(string text, DateTime now)
    {
        var (dayStart, dayEnd) = PeriodWindow(text);
        var includeWeekends = MentionsWeekend(text);
        var today = DateOnly.FromDateTime(now);

        // 1) 月份
        if (Regex.IsMatch(text, "下下个月|下下月"))
        {
            return MonthWindow(today.AddMonths(2), dayStart, dayEnd, includeWeekends, now);
        }

        if (Regex.IsMatch(text, "下个月|下月"))
        {
            return MonthWindow(today.AddMonths(1), dayStart, dayEnd, includeWeekends, now);
        }

        if (Regex.IsMatch(text, "这个月|本月|当月|这月"))
        {
            return MonthWindow(today, dayStart, dayEnd, includeWeekends, now);
        }

        // 2) 周末：最近的周六（周日当天则从当天算）
        if (Regex.IsMatch(text, "周末"))
        {
            return WeekendWindow(now, dayStart, dayEnd);
        }

        // 3) 具体某天（含「下周三」「这周五」）
        if (TryResolveWeekday(text, now, out var weekday))
        {
            return DayWindow(weekday, dayStart, dayEnd, includeWeekends, now);
        }

        // 3) 整周
        if (Regex.IsMatch(text, "下下周|下下个星期"))
        {
            return WeekWindow(now, 2, dayStart, dayEnd, includeWeekends);
        }

        if (Regex.IsMatch(text, "下周|下个?星期"))
        {
            return WeekWindow(now, 1, dayStart, dayEnd, includeWeekends);
        }

        if (Regex.IsMatch(text, "这周|本周|这个?星期|这星期"))
        {
            return WeekWindow(now, 0, dayStart, dayEnd, includeWeekends);
        }

        // 4) 今天/明天/具体日期
        var parsed = ChineseTimeParser.Parse(text, now);
        if (parsed.Date is DateOnly date)
        {
            return DayWindow(date, dayStart, dayEnd, includeWeekends, now);
        }

        // 5) 没提时间：看接下来一周
        var start = now.Date;
        return new TimeWindow(
            start,
            start.AddDays(7),
            dayStart,
            dayEnd,
            $"接下来一周（{AssistantFormat.Range(start, start.AddDays(6))}）",
            includeWeekends);
    }

    /// <summary>解析「把 X 改到 Y」。无法解析返回 null。</summary>
    public static MoveRequest? ParseMove(string text, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var (index, keyword, earlier) = FindMoveKeyword(text);
        if (index < 0)
        {
            return null;
        }

        var before = text[..index];
        var after = text[(index + keyword.Length)..].TrimStart(' ', '，', ',', '：', ':', '到', '至', '在', '于', '为');

        var titleHint = ExtractTitleHint(before);
        var dayHint = ParseDate(before, now);
        var newDate = ParseDate(after, now);
        var newTime = ParseTime(after, now);
        var period = FindPeriod(after.Length > 0 ? after : text);
        var shift = ParseShift(text, earlier);

        if (newDate is null && newTime is null && shift == 0)
        {
            return null;
        }

        return new MoveRequest(titleHint, dayHint, newDate, newTime, period, shift);
    }

    /// <summary>取消类指令的目标：标题线索 + 日期线索。</summary>
    public static (string? TitleHint, DateOnly? DayHint) ParseTarget(string text, DateTime now)
    {
        var cleaned = Regex.Replace(text, "取消|删掉|删除|不去了|一下吧|一下|吧", "");
        return (ExtractTitleHint(cleaned), ParseDate(cleaned, now));
    }

    private static (int Index, string Word, bool Earlier) FindMoveKeyword(string text)
    {
        var best = (-1, string.Empty, false);
        foreach (var (word, earlier) in MoveKeywords)
        {
            var index = text.IndexOf(word, StringComparison.Ordinal);
            if (index < 0)
            {
                continue;
            }

            if (best.Item1 < 0 || index < best.Item1 || (index == best.Item1 && word.Length > best.Item2.Length))
            {
                best = (index, word, earlier);
            }
        }

        return best;
    }

    private static DateOnly? ParseDate(string text, DateTime now)
        => string.IsNullOrWhiteSpace(text) ? null : ChineseTimeParser.Parse(text, now).Date;

    private static TimeOnly? ParseTime(string text, DateTime now)
        => string.IsNullOrWhiteSpace(text) ? null : ChineseTimeParser.Parse(text, now).Start;

    /// <summary>「推迟一小时」「提前 30 分钟」的位移，单位分钟。</summary>
    private static int ParseShift(string text, bool earlier)
    {
        var match = Regex.Match(
            text,
            @"(?:推迟|延后|推后|往后推|提前|提早)\s*(?<n>[0-9一二两三四五六七八九十半.]+)?\s*个?\s*(?<unit>小时|钟头|分钟)");

        if (!match.Success)
        {
            return 0;
        }

        var number = ParseNumber(match.Groups["n"].Value);
        var minutes = match.Groups["unit"].Value == "分钟" ? number : number * 60;
        if (minutes <= 0)
        {
            minutes = 60;
        }

        var total = (int)Math.Round(minutes);
        return earlier ? -total : total;
    }

    private static double ParseNumber(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 1;
        }

        if (value == "半")
        {
            return 0.5;
        }

        var digits = new Dictionary<char, int>
        {
            ['一'] = 1, ['二'] = 2, ['两'] = 2, ['三'] = 3, ['四'] = 4,
            ['五'] = 5, ['六'] = 6, ['七'] = 7, ['八'] = 8, ['九'] = 9
        };

        if (value.Length == 1 && digits.TryGetValue(value[0], out var single))
        {
            return single;
        }

        if (value == "十")
        {
            return 10;
        }

        if (value.StartsWith('十'))
        {
            return 10 + (value.Length > 1 && digits.TryGetValue(value[1], out var rest) ? rest : 0);
        }

        var tenIndex = value.IndexOf('十');
        if (tenIndex > 0)
        {
            var tens = digits.TryGetValue(value[0], out var head) ? head : 1;
            var ones = value.Length > tenIndex + 1 && digits.TryGetValue(value[tenIndex + 1], out var tail) ? tail : 0;
            return tens * 10 + ones;
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed : 1;
    }

    private static string? ExtractTitleHint(string text)
    {
        var quoted = Regex.Match(text, @"[「『""'‘“](?<t>[^」』""'’”]+)[」』""'’”]");
        if (quoted.Success)
        {
            var inside = quoted.Groups["t"].Value.Trim();
            return inside.Length >= 1 ? inside : null;
        }

        var value = Regex.Replace(text, @"^(请|麻烦|帮我|帮忙|帮|我要|我想|能不能|可以|把|将|给)", "");
        value = Regex.Replace(value, @"(下下个星期|下下星期|下下周|下个星期|下星期|下周|这个星期|这星期|这周|本周|星期|礼拜|周)[一二三四五六日天]", "");
        value = Regex.Replace(value, @"今天|明天|后天|大后天|今日|明日|\d{1,2}月\d{1,2}[日号]|\d{1,2}[日号]", "");
        value = Regex.Replace(value, @"上午|下午|晚上|中午|早上|早晨|凌晨", "");
        value = Regex.Replace(value, @"(的|地|了|吧|呢|啊)", "");
        value = value.Trim(' ', '，', ',', '。', '、', '：', ':', '；', ';');

        return value.Length >= 2 ? value : null;
    }

    private static string? FindPeriod(string text)
    {
        var candidates = new[] { "上午", "早上", "早晨", "中午", "下午", "晚上" };
        var best = (Index: int.MaxValue, Period: (string?)null);

        foreach (var candidate in candidates)
        {
            var index = text.IndexOf(candidate, StringComparison.Ordinal);
            if (index >= 0 && index < best.Index)
            {
                best = (index, candidate);
            }
        }

        return best.Period switch
        {
            "早上" or "早晨" => "上午",
            _ => best.Period
        };
    }

    private static (TimeSpan Start, TimeSpan End) PeriodWindow(string text)
    {
        var period = FindPeriod(text);
        return period switch
        {
            "上午" => (new TimeSpan(9, 0, 0), new TimeSpan(12, 0, 0)),
            "中午" => (new TimeSpan(12, 0, 0), new TimeSpan(14, 0, 0)),
            "下午" => (new TimeSpan(13, 0, 0), new TimeSpan(18, 0, 0)),
            "晚上" => (new TimeSpan(18, 0, 0), new TimeSpan(22, 0, 0)),
            _ => (new TimeSpan(9, 0, 0), new TimeSpan(18, 0, 0))
        };
    }

    private static bool MentionsWeekend(string text)
        => Regex.IsMatch(text, "周末|星期六|星期日|礼拜六|礼拜日|礼拜天|周六|周日");

    private static bool TryResolveWeekday(string text, DateTime now, out DateOnly day)
    {
        day = default;
        var match = WeekdayRegex.Match(text);
        if (!match.Success)
        {
            return false;
        }

        var iso = IsoFromChineseWeekday(match.Groups["w"].Value);
        if (iso <= 0)
        {
            return false;
        }

        var today = DateOnly.FromDateTime(now);
        var todayIso = ((int)now.DayOfWeek + 6) % 7 + 1;
        var monday = today.AddDays(-(todayIso - 1));
        var pre = match.Groups["pre"].Value;
        var offset = pre.Contains("下下") ? 2 : pre.StartsWith('下') ? 1 : 0;

        day = offset > 0
            ? monday.AddDays(7 * offset + iso - 1)
            : monday.AddDays(iso - 1);

        if (offset == 0 && day < today)
        {
            day = day.AddDays(7);
        }

        return true;
    }

    private static int IsoFromChineseWeekday(string value) => value switch
    {
        "一" => 1,
        "二" => 2,
        "三" => 3,
        "四" => 4,
        "五" => 5,
        "六" => 6,
        "日" or "天" => 7,
        _ => 0
    };

    private static TimeWindow DayWindow(
        DateOnly day,
        TimeSpan dayStart,
        TimeSpan dayEnd,
        bool includeWeekends,
        DateTime now)
    {
        var start = day.ToDateTime(TimeOnly.MinValue);
        return new TimeWindow(
            start,
            start.AddDays(1),
            dayStart,
            dayEnd,
            AssistantFormat.DayLabel(start, now),
            includeWeekends || day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
    }

    private static TimeWindow WeekendWindow(DateTime now, TimeSpan dayStart, TimeSpan dayEnd)
    {
        var today = now.Date;
        var daysUntilSaturday = ((int)DayOfWeek.Saturday - (int)now.DayOfWeek + 7) % 7;
        var saturday = now.DayOfWeek == DayOfWeek.Sunday ? today : today.AddDays(daysUntilSaturday);
        var end = saturday.AddDays(now.DayOfWeek == DayOfWeek.Sunday ? 1 : 2);

        return new TimeWindow(
            saturday,
            end,
            dayStart,
            dayEnd,
            $"周末（{AssistantFormat.Range(saturday, end.AddDays(-1))}）",
            true);
    }

    private static TimeWindow WeekWindow(        DateTime now,
        int offsetWeeks,
        TimeSpan dayStart,
        TimeSpan dayEnd,
        bool includeWeekends)
    {
        var today = DateOnly.FromDateTime(now);
        var todayIso = ((int)now.DayOfWeek + 6) % 7 + 1;
        var monday = today.AddDays(-(todayIso - 1)).AddDays(7 * offsetWeeks);
        var start = monday.ToDateTime(TimeOnly.MinValue);
        var label = offsetWeeks switch
        {
            0 => $"这周（{AssistantFormat.Range(start, start.AddDays(6))}）",
            1 => $"下周（{AssistantFormat.Range(start, start.AddDays(6))}）",
            _ => $"下下周（{AssistantFormat.Range(start, start.AddDays(6))}）"
        };

        return new TimeWindow(start, start.AddDays(7), dayStart, dayEnd, label, includeWeekends);
    }

    private static TimeWindow MonthWindow(
        DateOnly dayInMonth,
        TimeSpan dayStart,
        TimeSpan dayEnd,
        bool includeWeekends,
        DateTime now)
    {
        var start = new DateOnly(dayInMonth.Year, dayInMonth.Month, 1);
        var end = start.AddMonths(1);
        return new TimeWindow(
            start.ToDateTime(TimeOnly.MinValue),
            end.ToDateTime(TimeOnly.MinValue),
            dayStart,
            dayEnd,
            $"{start.Month} 月（{AssistantFormat.Range(start.ToDateTime(TimeOnly.MinValue), end.AddDays(-1).ToDateTime(TimeOnly.MinValue))}）",
            includeWeekends);
    }
}
