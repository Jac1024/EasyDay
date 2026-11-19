using System.Text;
using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.Core.Assistant;

/// <summary>
/// 自然语言查询与改期。纯本地计算：
/// - 查询空闲：展开重复日程后按工作时段算空档，结果精确、离线可用
/// - 改期/取消：规则解析 + 事件匹配，分不清时给候选让用户点选
/// 写库由调用方在用户确认后执行（和 AI 草稿卡一样，助手不直接改日历）。
/// </summary>
public sealed class AssistantService
{
    public AssistantResult Interpret(
        string text,
        IReadOnlyList<CalendarEvent> events,
        DateTime now,
        int? preferEventId = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new AssistantResult
            {
                Intent = AssistantIntent.Unknown,
                Message = "说一句话试试：「这周还有空吗」「把周三的会挪到周四下午」。"
            };
        }

        var input = text.Trim();
        var intent = AssistantText.DetectIntent(input);

        return intent switch
        {
            AssistantIntent.Query => AnswerAvailability(input, events, now),
            AssistantIntent.Reschedule => BuildReschedule(input, events, now, preferEventId),
            AssistantIntent.Cancel => BuildCancel(input, events, now, preferEventId),
            _ => new AssistantResult
            {
                Intent = AssistantIntent.Create,
                Message = "这看起来是新建日程。点下面的「用 AI 新建」，会生成一张可编辑的确认卡。"
            }
        };
    }

    private static AssistantResult AnswerAvailability(
        string input,
        IReadOnlyList<CalendarEvent> events,
        DateTime now)
    {
        var window = AssistantText.ParseWindow(input, now);
        var slots = AvailabilityService.FindFreeSlots(events, window, now);
        var related = RecurrenceExpander
            .ForRange(events.Where(e => e.Status == EventStatus.Active), window.Start, window.End)
            .Where(o => o.Source.Status == EventStatus.Active)
            .OrderBy(o => o.Start)
            .ToList();

        return new AssistantResult
        {
            Intent = AssistantIntent.Query,
            Message = AvailabilityService.Summarize(slots, related, window, now),
            FreeSlots = slots,
            Related = related
        };
    }

    private static AssistantResult BuildReschedule(
        string input,
        IReadOnlyList<CalendarEvent> events,
        DateTime now,
        int? preferEventId)
    {
        var request = AssistantText.ParseMove(input, now);
        if (request is null || !request.HasNewTime)
        {
            return new AssistantResult
            {
                Intent = AssistantIntent.Reschedule,
                Message = "没听出要改到什么时候。试试：「把周三的会挪到周四下午」或「把明天的评审推迟一小时」。"
            };
        }

        var match = MatchOrExplain(request.TitleHint, request.DayHint, events, now, preferEventId,
            AssistantIntent.Reschedule);
        if (match.Best is null)
        {
            return match.Result!;
        }

        var target = match.Best!;
        var newStart = request.ResolveStart(target.Start);
        if (newStart is null)
        {
            return new AssistantResult
            {
                Intent = AssistantIntent.Reschedule,
                Message = "没算出新的时间。可以说得更具体，例如「挪到周四下午 3 点」。",
                Target = target
            };
        }

        var isAllDay = target.AllDay;
        var duration = target.End > target.Start ? target.End - target.Start : TimeSpan.FromHours(1);
        if (isAllDay)
        {
            newStart = newStart.Value.Date;
        }

        var newEnd = isAllDay ? newStart.Value.AddDays(1) : newStart.Value + duration;
        var conflicts = isAllDay
            ? new List<EventOccurrence>()
            : ConflictDetector.FindConflicts(newStart.Value, newEnd, events, target.Id);

        var matched = match.Occurrence;
        var builder = new StringBuilder();
        builder.Append($"找到「{target.Title}」：");
        builder.Append(matched is null
            ? AssistantFormat.Clock(target.Start, target.End)
            : AssistantFormat.Span(matched.Start, matched.End, now));
        builder.AppendLine();
        builder.Append("改到 ");
        builder.Append(isAllDay
            ? $"{AssistantFormat.DayLabel(newStart.Value, now)}（全天）"
            : $"{AssistantFormat.When(newStart.Value, newEnd, now)}（{AssistantFormat.Duration(duration)}）");

        if (conflicts.Count > 0)
        {
            builder.AppendLine();
            builder.Append($"⚠ 与 {conflicts.Count} 个日程重叠：");
            builder.Append(string.Join("、", conflicts.Take(3).Select(c =>
                $"{c.Start:HH:mm} {c.Source.Title}")));
        }

        if (target.IsRecurring)
        {
            builder.AppendLine();
            builder.Append("这是重复日程，会移动整个系列（重复规则保持不变）。");
        }

        return new AssistantResult
        {
            Intent = AssistantIntent.Reschedule,
            Message = builder.ToString(),
            Target = target,
            NewStart = newStart,
            NewEnd = newEnd,
            Related = match.Candidates,
            Conflicts = conflicts
        };
    }

    private static AssistantResult BuildCancel(
        string input,
        IReadOnlyList<CalendarEvent> events,
        DateTime now,
        int? preferEventId)
    {
        var (titleHint, dayHint) = AssistantText.ParseTarget(input, now);
        var match = MatchOrExplain(titleHint, dayHint, events, now, preferEventId, AssistantIntent.Cancel);
        if (match.Best is null)
        {
            return match.Result!;
        }

        var target = match.Best!;
        var occurrence = match.Occurrence;
        var builder = new StringBuilder();
        builder.Append($"要取消「{target.Title}」");
        if (occurrence is not null)
        {
            builder.Append($"（{AssistantFormat.Span(occurrence.Start, occurrence.End, now)}）");
        }

        builder.Append(" 吗？");
        if (target.IsRecurring)
        {
            builder.AppendLine();
            builder.Append("这是重复日程，取消后整个系列都会停止。");
        }

        return new AssistantResult
        {
            Intent = AssistantIntent.Cancel,
            Message = builder.ToString(),
            Target = target,
            Related = match.Candidates
        };
    }

    private static (CalendarEvent? Best, EventOccurrence? Occurrence, IReadOnlyList<EventOccurrence> Candidates, AssistantResult? Result)
        MatchOrExplain(
            string? titleHint,
            DateOnly? dayHint,
            IReadOnlyList<CalendarEvent> events,
            DateTime now,
            int? preferEventId,
            AssistantIntent intent)
    {
        var match = EventMatcher.Match(titleHint, dayHint, events, now, preferEventId);

        if (match.Best is not null)
        {
            return (match.Best, match.BestOccurrence, match.Candidates, null);
        }

        if (match.Candidates.Count == 0)
        {
            var hint = intent == AssistantIntent.Cancel ? "取消" : "改期";
            return (null, null, Array.Empty<EventOccurrence>(), new AssistantResult
            {
                Intent = intent,
                Message = $"没找到要{hint}的日程。可以说得具体些，例如「{ExampleFor(intent)}」。"
            });
        }

        return (null, null, match.Candidates, new AssistantResult
        {
            Intent = intent,
            Message = $"找到 {match.Candidates.Count} 个可能的日程，点一个继续：",
            Related = match.Candidates,
            NeedsClarification = true
        });
    }

    private static string ExampleFor(AssistantIntent intent)
        => intent == AssistantIntent.Cancel
            ? "取消周三的项目周会"
            : "把周三的项目周会挪到周四下午";
}
