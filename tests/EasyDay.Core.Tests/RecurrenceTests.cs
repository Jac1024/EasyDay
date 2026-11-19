using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.Core.Tests;

public class RecurrenceTests
{
    private static CalendarEvent Weekly()
    {
        return new CalendarEvent
        {
            Id = 1,
            Title = "晨会",
            Start = new DateTime(2026, 11, 16, 9, 0, 0), // 周一
            End = new DateTime(2026, 11, 16, 9, 30, 0),
            RecurrenceRule = "FREQ=WEEKLY;INTERVAL=1;BYDAY=MO,WE,FR"
        };
    }

    [Fact]
    public void Expands_weekly_byday()
    {
        var occurrences = RecurrenceExpander
            .Expand(Weekly(), new DateTime(2026, 11, 16), new DateTime(2026, 11, 23))
            .Select(o => o.Start)
            .ToList();

        Assert.Equal(3, occurrences.Count);
        Assert.Equal(new DateTime(2026, 11, 16, 9, 0, 0), occurrences[0]);
        Assert.Equal(new DateTime(2026, 11, 18, 9, 0, 0), occurrences[1]);
        Assert.Equal(new DateTime(2026, 11, 20, 9, 0, 0), occurrences[2]);
    }

    [Fact]
    public void Respects_until()
    {
        var item = Weekly();
        item.RecurrenceRule = "FREQ=DAILY;INTERVAL=1;UNTIL=20261118";

        var occurrences = RecurrenceExpander
            .Expand(item, new DateTime(2026, 11, 16), new DateTime(2026, 11, 25))
            .Select(o => o.Start.Date)
            .ToList();

        Assert.Equal(3, occurrences.Count); // 16、17、18
    }

    [Fact]
    public void Single_event_outside_range_is_skipped()
    {
        var item = new CalendarEvent
        {
            Start = new DateTime(2026, 11, 20, 10, 0, 0),
            End = new DateTime(2026, 11, 20, 11, 0, 0)
        };

        var occurrences = RecurrenceExpander
            .Expand(item, new DateTime(2026, 11, 21), new DateTime(2026, 11, 22))
            .ToList();

        Assert.Empty(occurrences);
    }

    [Fact]
    public void Detects_conflict_with_recurring_event()
    {
        var existing = new[] { Weekly() };

        var conflicts = ConflictDetector.FindConflicts(
            new DateTime(2026, 11, 18, 9, 15, 0),
            new DateTime(2026, 11, 18, 10, 0, 0),
            existing);

        Assert.Single(conflicts);
    }

    [Fact]
    public void Adjacent_event_is_not_a_conflict()
    {
        var existing = new[]
        {
            new CalendarEvent
            {
                Id = 2,
                Start = new DateTime(2026, 11, 18, 9, 0, 0),
                End = new DateTime(2026, 11, 18, 10, 0, 0)
            }
        };

        var conflicts = ConflictDetector.FindConflicts(
            new DateTime(2026, 11, 18, 10, 0, 0),
            new DateTime(2026, 11, 18, 11, 0, 0),
            existing);

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Suggests_meeting_reminders()
    {
        var draft = new EventDraft
        {
            Title = "项目评审",
            Start = new DateTime(2026, 11, 18, 15, 0, 0),
            End = new DateTime(2026, 11, 18, 16, 0, 0)
        };

        var offsets = ReminderPlanner.Suggest(draft);

        Assert.Contains(15, offsets);
        Assert.Contains(1440, offsets);
    }

    [Fact]
    public void Suggests_deadline_reminders_for_allday_deadline()
    {
        var draft = new EventDraft
        {
            Title = "提交季度报表截止",
            AllDay = true,
            Start = new DateTime(2026, 11, 20),
            End = new DateTime(2026, 11, 21)
        };

        var offsets = ReminderPlanner.Suggest(draft);

        Assert.Contains(4320, offsets);
    }
}
