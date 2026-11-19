using System.Text.RegularExpressions;

namespace EasyDay.Core.Parsing;

public sealed class TimeParseResult
{
    public DateOnly? Date { get; set; }
    public bool HasExplicitDate { get; set; }
    public TimeOnly? Start { get; set; }
    public TimeOnly? End { get; set; }
    public bool HasExplicitTime => Start.HasValue;
    public string? Title { get; set; }
    public string? Location { get; set; }
    public int? ReminderMinutes { get; set; }
}

/// <summary>
/// 轻量中文时间解析：AI 不可用时的兜底，也用于给 AI 提供时间锚点。
/// 支持：今天/明天/后天/大后天、周X/下周X、M月D日、yyyy-MM-dd、
/// 上午/下午/晚上 + H点(半)/H:MM、时间段、N小时、提前N分钟。
/// </summary>
public static class ChineseTimeParser
{
    private static readonly Regex TimeRegex = new(
        @"(?<p>凌晨|早上|早晨|上午|中午|下午|傍晚|晚上|晚)?\s*(?<h>\d{1,2})\s*(?::|：|点|时)\s*(?<m>半|\d{1,2})?\s*分?",
        RegexOptions.Compiled);

    private static readonly Regex WeekdayRegex = new(
        @"(?<pre>下下周|下下个星期|下个?星期|下个?周|下周|这个?星期|这个?周|这周|星期|周|礼拜)(?<w>[一二三四五六日天])",
        RegexOptions.Compiled);

    private static readonly Regex DurationHourRegex = new(
        @"(?:时长|预计|大约|约|进行|开)?\s*(?<n>\d+(?:\.\d+)?)\s*个?\s*小时",
        RegexOptions.Compiled);

    private static readonly Regex DurationMinuteRegex = new(
        @"(?:时长|预计|大约|约|进行|开)?\s*(?<n>\d{1,3})\s*分钟",
        RegexOptions.Compiled);

    private static readonly Regex ReminderRegex = new(
        @"提前\s*(?<n>\d{1,3})\s*分钟",
        RegexOptions.Compiled);

    public static TimeParseResult Parse(string text, DateTime now)
    {
        var result = new TimeParseResult();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var working = Normalize(text);
        var today = DateOnly.FromDateTime(now);
        var matchedFragments = new List<string>();

        // ---------- 日期 ----------
        DateOnly? date = null;
        var hasDate = false;

        var absolute = Regex.Match(working, @"(?<y>\d{4})\s*[-/.年]\s*(?<mo>\d{1,2})\s*[-/.月]\s*(?<d>\d{1,2})\s*[日号]?");
        var monthDay = absolute.Success
            ? Match.Empty
            : Regex.Match(working, @"(?<mo>\d{1,2})\s*月\s*(?<d>\d{1,2})\s*[日号]?");

        if (absolute.Success || monthDay.Success)
        {
            var m = absolute.Success ? absolute : monthDay;
            var year = m.Groups["y"].Success ? int.Parse(m.Groups["y"].Value) : today.Year;
            var month = int.Parse(m.Groups["mo"].Value);
            var day = int.Parse(m.Groups["d"].Value);
            if (month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month))
            {
                date = new DateOnly(year, month, day);
                hasDate = true;
                matchedFragments.Add(m.Value);

                // 只写"1月5日"且已过去，则视为明年
                if (!m.Groups["y"].Success && date < today)
                {
                    date = date.Value.AddYears(1);
                }
            }
        }

        if (date is null)
        {
            var week = WeekdayRegex.Match(working);
            if (week.Success)
            {
                var targetIso = IsoFromChineseWeekday(week.Groups["w"].Value);
                if (targetIso > 0)
                {
                    var todayIso = ((int)now.DayOfWeek + 6) % 7 + 1;
                    var monday = today.AddDays(-(todayIso - 1));
                    var pre = week.Groups["pre"].Value;
                    var weekOffset = pre.Contains("下下") ? 2 : pre.StartsWith("下") ? 1 : 0;

                    if (weekOffset > 0)
                    {
                        date = monday.AddDays(7 * weekOffset + targetIso - 1);
                    }
                    else
                    {
                        date = monday.AddDays(targetIso - 1);
                        if (date < today)
                        {
                            date = date.Value.AddDays(7);
                        }
                    }

                    hasDate = true;
                    matchedFragments.Add(week.Value);
                }
            }
        }

        if (date is null)
        {
            if (working.Contains("大后天"))
            {
                date = today.AddDays(3);
                matchedFragments.Add("大后天");
            }
            else if (working.Contains("后天"))
            {
                date = today.AddDays(2);
                matchedFragments.Add("后天");
            }
            else if (Regex.IsMatch(working, "明天|明日|明早|明晚|明儿"))
            {
                date = today.AddDays(1);
                matchedFragments.Add("明天");
            }
            else if (Regex.IsMatch(working, "今天|今日|今晚|今早|今儿"))
            {
                date = today;
                matchedFragments.Add("今天");
            }

            hasDate = date.HasValue;
        }

        if (date is null)
        {
            var dayOnly = Regex.Match(working, @"(?<d>\d{1,2})\s*[日号]");
            if (dayOnly.Success)
            {
                var day = int.Parse(dayOnly.Groups["d"].Value);
                var month = today.Month;
                var year = today.Year;
                if (day >= 1 && day <= DateTime.DaysInMonth(year, month))
                {
                    date = new DateOnly(year, month, day);
                    if (date < today)
                    {
                        var next = today.AddMonths(1);
                        if (day <= DateTime.DaysInMonth(next.Year, next.Month))
                        {
                            date = new DateOnly(next.Year, next.Month, day);
                        }
                    }

                    hasDate = true;
                    matchedFragments.Add(dayOnly.Value);
                }
            }
        }

        result.Date = date;
        result.HasExplicitDate = hasDate;

        // ---------- 时间 ----------
        var times = new List<(Match Match, TimeOnly Value)>();
        foreach (Match m in TimeRegex.Matches(working))
        {
            if (TryToTime(m.Groups["p"].Value, m.Groups["h"].Value, m.Groups["m"].Value, out var time))
            {
                times.Add((m, time));
            }
        }

        if (times.Count > 0)
        {
            var first = times[0];
            var start = first.Value;
            matchedFragments.Add(first.Match.Value);

            TimeOnly? end = null;
            if (times.Count > 1)
            {
                var second = times[1];
                var between = working.Substring(
                    first.Match.Index + first.Match.Length,
                    Math.Max(0, second.Match.Index - (first.Match.Index + first.Match.Length)));

                if (Regex.IsMatch(between, @"^\s*(?:-|~|—|到|至|–)\s*$"))
                {
                    var endValue = second.Value;
                    if (endValue < start && start.Hour >= 12 && endValue.Hour < 12)
                    {
                        endValue = endValue.AddHours(12);
                    }

                    end = endValue;
                    matchedFragments.Add(second.Match.Value);
                }
            }

            if (end is null)
            {
                var hourDuration = DurationHourRegex.Match(working);
                if (hourDuration.Success && double.TryParse(hourDuration.Groups["n"].Value, out var hours) && hours is > 0 and <= 24)
                {
                    end = start.Add(TimeSpan.FromHours(hours));
                    matchedFragments.Add(hourDuration.Value);
                }
                else
                {
                    var minuteDuration = DurationMinuteRegex.Match(working);
                    if (minuteDuration.Success &&
                        int.TryParse(minuteDuration.Groups["n"].Value, out var minutes) &&
                        minutes is > 0 and <= 600 &&
                        Regex.IsMatch(working, "会议|开会|讨论|培训|面试|沟通|电话|评审|分享"))
                    {
                        end = start.Add(TimeSpan.FromMinutes(minutes));
                        matchedFragments.Add(minuteDuration.Value);
                    }
                }
            }

            result.Start = start;
            result.End = end;
        }

        var reminder = ReminderRegex.Match(working);
        if (reminder.Success && int.TryParse(reminder.Groups["n"].Value, out var reminderMinutes))
        {
            result.ReminderMinutes = reminderMinutes;
            matchedFragments.Add(reminder.Value);
        }

        // ---------- 地点 ----------
        var location = Regex.Match(working, @"(?:地点|地址|会议地点|会场)\s*[:：]\s*(?<l>[^\n，,。；;]{2,30})");
        if (!location.Success)
        {
            location = Regex.Match(working, @"会议室\s*[:：]\s*(?<l>[^\n，,。；;]{2,30})");
        }

        if (!location.Success)
        {
            location = Regex.Match(working, @"在\s*(?<l>[^\s，,。；;开召举办进见]{2,20})");
        }

        if (!location.Success)
        {
            location = Regex.Match(working, @"(?<l>[\w\u4e00-\u9fa5]{2,12}(?:会议室|教室|报告厅|大厦|酒店|广场|中心))");
        }

        if (location.Success)
        {
            var value = location.Groups["l"].Value.Trim();
            value = Regex.Replace(value, @"(?:开会|会议|集合|碰面)$", string.Empty).Trim();
            if (value.Length >= 2)
            {
                result.Location = value;
                matchedFragments.Add(location.Value);
            }
        }

        // ---------- 标题 ----------
        result.Title = GuessTitle(working, matchedFragments);
        return result;
    }

    public static string Normalize(string text)
    {
        return text
            .Replace('：', ':')
            .Replace('～', '~')
            .Replace('—', '-')
            .Replace('－', '-')
            .Replace('\r', '\n');
    }

    private static bool TryToTime(string period, string hourText, string minuteText, out TimeOnly time)
    {
        time = default;
        if (!int.TryParse(hourText, out var hour))
        {
            return false;
        }

        var minute = minuteText switch
        {
            "半" => 30,
            "" => 0,
            _ => int.TryParse(minuteText, out var m) ? m : 0
        };

        switch (period)
        {
            case "下午":
            case "傍晚":
            case "晚上":
            case "晚":
                if (hour < 12)
                {
                    hour += 12;
                }

                break;
            case "中午":
                if (hour is >= 1 and <= 5)
                {
                    hour += 12;
                }

                break;
            case "凌晨":
                if (hour == 12)
                {
                    hour = 0;
                }

                break;
        }

        if (hour is < 0 or > 23 || minute is < 0 or > 59)
        {
            return false;
        }

        time = new TimeOnly(hour, minute);
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

    private static string GuessTitle(string text, IEnumerable<string> fragments)
    {
        var subject = Regex.Match(text, @"(?:主题|议题|事项|内容|会议名称)\s*[:：]\s*(?<t>[^\n，,。；;]{2,40})");
        if (subject.Success)
        {
            return subject.Groups["t"].Value.Trim();
        }

        // "在 A301 召开项目启动会" / "组织一次培训" 这类动宾结构最可靠
        var verb = Regex.Match(text, @"(?:召开|举行|举办|组织|安排|开)\s*(?<t>[^，,。；;\n]{2,28})");
        if (verb.Success)
        {
            var value = CleanTitle(verb.Groups["t"].Value);
            if (value.Length >= 2)
            {
                return value;
            }
        }

        var line = text
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault() ?? string.Empty;

        foreach (var fragment in fragments.Where(f => f.Length > 0).OrderByDescending(f => f.Length))
        {
            line = line.Replace(fragment, " ", StringComparison.OrdinalIgnoreCase);
        }

        line = Regex.Replace(line, @"^(?:【[^】]*】|\[[^\]]*\])\s*", string.Empty);
        line = Regex.Replace(line, @"^[\w\u4e00-\u9fa5]{1,12}\s*[:：]\s*", string.Empty);
        line = Regex.Replace(line, @"@(?:所有人|全体成员)", string.Empty);
        line = Regex.Replace(line, @"^(?:通知|公告|提醒|安排)\s*[:：]?\s*", string.Empty);
        line = Regex.Replace(line, @"\s+", " ").Trim(' ', '，', ',', '。', '；', ';', ':', '-', '·', '、');
        line = CleanTitle(line);

        if (line.Length > 28)
        {
            line = line[..28];
        }

        return line;
    }

    private static string CleanTitle(string value)
    {
        var line = value.Trim();
        line = Regex.Replace(line, @"^(?:在|于|关于)\s*", string.Empty);
        line = Regex.Replace(line, @"(?:的?(?:通知|安排|事宜|相关信息))$", string.Empty);
        line = Regex.Replace(line, @"(?:请|务必|记得|需要).*$", string.Empty);
        line = Regex.Replace(line, @"\s+", " ");
        return line.Trim(' ', '，', ',', '。', '；', ';', ':', '-', '·', '、');
    }
}
