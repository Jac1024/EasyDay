using EasyDay.Core.Models;

namespace EasyDay.Core.Models;

/// <summary>
/// 一条待确认的日程草稿。AI 永不直接写入日历，必须经过这个对象再由用户确认。
/// </summary>
public sealed class EventDraft
{
    public string Title { get; set; } = string.Empty;

    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    public bool AllDay { get; set; }

    public string? Location { get; set; }

    public string? Notes { get; set; }

    /// <summary>提醒偏移（提前多少分钟）。</summary>
    public List<int> ReminderMinutes { get; set; } = new();

    public double Confidence { get; set; } = 1d;

    public string? RawText { get; set; }

    public EventSource Source { get; set; } = EventSource.Manual;

    public string? RecurrenceRule { get; set; }

    /// <summary>校验/解析提示，UI 用橙色展示。</summary>
    public List<string> Warnings { get; } = new();

    /// <summary>AI 认为信息不明确时提出的追问，用户补充后会重新识别。</summary>
    public List<ClarificationQuestion> Questions { get; } = new();

    /// <summary>UI 勾选状态。</summary>
    public bool IsSelected { get; set; } = true;

    /// <summary>修正明显不合法的字段，并把处理结果写进 Warnings。</summary>
    public void Normalize(DateTime? now = null)
    {
        Warnings.Clear();

        if (string.IsNullOrWhiteSpace(Title))
        {
            Title = "未命名日程";
            Warnings.Add("未识别到标题，已使用默认标题");
        }

        Title = Title.Trim();

        if (Start == default)
        {
            var reference = now ?? DateTime.Now;
            Start = reference.Date.AddHours(9);
            Warnings.Add("未识别到时间，默认今天 09:00");
        }

        if (AllDay)
        {
            Start = Start.Date;
            End = Start.Date.AddDays(1);
        }
        else
        {
            if (End <= Start)
            {
                End = Start.AddHours(1);
                Warnings.Add("结束时间不合法，已按 1 小时处理");
            }
        }

        if (Confidence is < 0 or > 1)
        {
            Confidence = Math.Clamp(Confidence, 0, 1);
        }

        if (Location is { Length: 0 })
        {
            Location = null;
        }

        if (Notes is { Length: 0 })
        {
            Notes = null;
        }
    }

    public CalendarEvent ToEvent() => new()
    {
        Title = Title,
        Start = Start,
        End = End,
        AllDay = AllDay,
        Location = Location,
        Notes = Notes,
        Source = Source,
        RawText = RawText,
        Confidence = Confidence,
        Status = EventStatus.Active,
        RecurrenceRule = RecurrenceRule
    };
}

/// <summary>草稿留痕，便于回看"这条日程是从哪条消息来的"以及评估解析质量。</summary>
public sealed class DraftRecord
{
    public int Id { get; set; }

    public string RawInput { get; set; } = string.Empty;

    public string? ParsedJson { get; set; }

    public double Confidence { get; set; }

    public DraftStatus Status { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int? CalendarEventId { get; set; }
}

/// <summary>AI 对模糊信息提出的一个追问。</summary>
public sealed class ClarificationQuestion
{
    public string Field { get; set; } = string.Empty;

    public string Question { get; set; } = string.Empty;

    public List<string> Options { get; set; } = new();
}
