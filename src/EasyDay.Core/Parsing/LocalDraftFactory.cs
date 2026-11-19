using EasyDay.Core.Models;

namespace EasyDay.Core.Parsing;

/// <summary>没有 AI 时的本地兜底：用规则解析文本，置信度低，靠确认卡兜住。</summary>
public static class LocalDraftFactory
{
    public static EventDraft Build(string text, DateTime now, EventSource source = EventSource.Manual)
    {
        var parsed = ChineseTimeParser.Parse(text, now);
        var date = parsed.Date ?? DateOnly.FromDateTime(now);

        var draft = new EventDraft
        {
            Source = source,
            RawText = text,
            Title = string.IsNullOrWhiteSpace(parsed.Title) ? "未命名日程" : parsed.Title!,
            Location = parsed.Location,
            Notes = text.Trim()
        };

        if (parsed.Start is { } startTime)
        {
            var start = date.ToDateTime(startTime);
            var end = parsed.End is { } endTime
                ? date.ToDateTime(endTime)
                : start.AddHours(1);

            if (end <= start)
            {
                end = end.AddDays(1);
            }

            // "下午三点" 这种没写日期且时间已过的情况，顺延到明天
            if (!parsed.HasExplicitDate && start < now)
            {
                start = start.AddDays(1);
                end = end.AddDays(1);
                draft.Warnings.Add("时间已过，已顺延到明天");
            }

            draft.Start = start;
            draft.End = end;
            draft.AllDay = false;
        }
        else
        {
            draft.AllDay = true;
            draft.Start = date.ToDateTime(TimeOnly.MinValue);
            draft.End = draft.Start.AddDays(1);
        }

        if (parsed.ReminderMinutes is { } minutes)
        {
            draft.ReminderMinutes.Add(minutes);
        }

        draft.Confidence = 0.45
            + (parsed.HasExplicitDate ? 0.15 : 0)
            + (parsed.HasExplicitTime ? 0.2 : 0);

        draft.Normalize(now);

        if (!parsed.HasExplicitTime)
        {
            draft.Questions.Add(new ClarificationQuestion
            {
                Field = "time",
                Question = "没识别到具体时间，补一句就行（例如：明天下午 3 点）"
            });
        }

        if (!parsed.HasExplicitDate)
        {
            draft.Warnings.Add("未识别到明确日期，默认今天");
        }

        if (!parsed.HasExplicitTime)
        {
            draft.Warnings.Add("未识别到具体时间，按全天处理");
        }

        return draft;
    }
}
