namespace EasyDay.Core.Models;

/// <summary>一条日程。时间统一按本机本地时间存储（不做多时区换算）。</summary>
public sealed class CalendarEvent
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime Start { get; set; }

    public DateTime End { get; set; }

    public bool AllDay { get; set; }

    public string? Location { get; set; }

    public string? Notes { get; set; }

    public EventSource Source { get; set; } = EventSource.Manual;

    /// <summary>来源原文（截图 OCR 文本 / 剪贴板文本），用于回溯。</summary>
    public string? RawText { get; set; }

    /// <summary>AI 解析置信度 0~1；手工录入为 1。</summary>
    public double Confidence { get; set; } = 1d;

    public EventStatus Status { get; set; } = EventStatus.Active;

    /// <summary>RFC 5545 RRULE 子集，例如 FREQ=WEEKLY;INTERVAL=1;BYDAY=MO,WE。空表示不重复。</summary>
    public string? RecurrenceRule { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public List<Reminder> Reminders { get; set; } = new();

    public bool IsRecurring => !string.IsNullOrWhiteSpace(RecurrenceRule);
}

/// <summary>一次提醒。FireAt 在保存时算好，编辑日程会重建。</summary>
public sealed class Reminder
{
    public int Id { get; set; }

    public int CalendarEventId { get; set; }

    public DateTime FireAt { get; set; }

    public bool Fired { get; set; }

    public int SnoozeCount { get; set; }

    public CalendarEvent? Event { get; set; }
}
