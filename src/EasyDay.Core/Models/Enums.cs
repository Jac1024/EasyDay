namespace EasyDay.Core.Models;

/// <summary>日程来源。用于"可解释"：每条日程都能回答它是从哪来的。</summary>
public enum EventSource
{
    Manual = 0,
    QuickAdd = 1,
    Clipboard = 2,
    Screenshot = 3,
    Import = 4
}

public enum EventStatus
{
    Active = 0,
    Completed = 1,
    Cancelled = 2
}

public enum DraftStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2
}
