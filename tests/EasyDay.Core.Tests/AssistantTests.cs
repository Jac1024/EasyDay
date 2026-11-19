using EasyDay.Core.Assistant;
using EasyDay.Core.Models;

namespace EasyDay.Core.Tests;

public class AssistantTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 15, 40, 0); // 周三

    private static CalendarEvent Event(
        int id,
        string title,
        DateTime start,
        TimeSpan duration,
        bool allDay = false,
        string? rrule = null) => new()
    {
        Id = id,
        Title = title,
        Start = start,
        End = allDay ? start.Date.AddDays(1) : start + duration,
        AllDay = allDay,
        RecurrenceRule = rrule
    };

    [Theory]
    [InlineData("这周还有空吗", AssistantIntent.Query)]
    [InlineData("明天下午有空吗？", AssistantIntent.Query)]
    [InlineData("把周三的会挪到周四下午", AssistantIntent.Reschedule)]
    [InlineData("把项目周会提前半小时", AssistantIntent.Reschedule)]
    [InlineData("取消周三的会", AssistantIntent.Cancel)]
    [InlineData("明天下午三点开会，记得带电脑", AssistantIntent.Create)]
    public void Detects_intent(string text, AssistantIntent expected)
        => Assert.Equal(expected, AssistantText.DetectIntent(text));

    [Fact]
    public void This_week_window_runs_monday_to_sunday()
    {
        var window = AssistantText.ParseWindow("这周还有空吗", Now);

        Assert.Equal(new DateTime(2026, 11, 16), window.Start);
        Assert.Equal(new DateTime(2026, 11, 23), window.End);
        Assert.Equal(new TimeSpan(9, 0, 0), window.DayStart);
        Assert.False(window.IncludeWeekends);
    }

    [Fact]
    public void Tomorrow_afternoon_window_is_single_day()
    {
        var window = AssistantText.ParseWindow("明天下午有空吗", Now);

        Assert.Equal(new DateTime(2026, 11, 19), window.Start);
        Assert.Equal(new DateTime(2026, 11, 20), window.End);
        Assert.Equal(new TimeSpan(13, 0, 0), window.DayStart);
        Assert.Equal(new TimeSpan(18, 0, 0), window.DayEnd);
    }

    [Fact]
    public void Next_week_weekday_resolves_to_next_week()
    {
        var window = AssistantText.ParseWindow("下周三有事吗", Now);

        Assert.Equal(new DateTime(2026, 11, 25), window.Start);
        Assert.Equal(new DateTime(2026, 11, 26), window.End);
    }

    [Fact]
    public void Free_slots_merge_busy_events()
    {
        var tomorrow = new DateTime(2026, 11, 19);
        var events = new[]
        {
            Event(1, "项目周会", tomorrow.AddHours(10), TimeSpan.FromHours(1)),
            Event(2, "和客户对需求", tomorrow.AddHours(14).AddMinutes(30), TimeSpan.FromHours(1))
        };

        var window = AssistantText.ParseWindow("明天", Now);
        var slots = AvailabilityService.FindFreeSlots(events, window, Now, 30);

        Assert.Equal(3, slots.Count);
        Assert.Equal(tomorrow.AddHours(9), slots[0].Start);
        Assert.Equal(tomorrow.AddHours(10), slots[0].End);
        Assert.Equal(tomorrow.AddHours(11), slots[1].Start);
        Assert.Equal(tomorrow.AddHours(14).AddMinutes(30), slots[1].End);
        Assert.Equal(tomorrow.AddHours(15).AddMinutes(30), slots[2].Start);
        Assert.Equal(tomorrow.AddHours(18), slots[2].End);
    }

    [Fact]
    public void All_day_event_blocks_whole_day()
    {
        var tomorrow = new DateTime(2026, 11, 19);
        var events = new[]
        {
            Event(1, "出差", tomorrow, TimeSpan.FromDays(1), allDay: true)
        };

        var window = AssistantText.ParseWindow("明天", Now);
        Assert.Empty(AvailabilityService.FindFreeSlots(events, window, Now));
    }

    [Fact]
    public void Weekend_is_skipped_unless_asked()
    {
        var events = Array.Empty<CalendarEvent>();
        var week = AssistantText.ParseWindow("这周", Now);
        var weekend = AssistantText.ParseWindow("周末有空吗", Now);

        var weekSlots = AvailabilityService.FindFreeSlots(events, week, Now);
        var weekendSlots = AvailabilityService.FindFreeSlots(events, weekend, Now);

        Assert.DoesNotContain(weekSlots, s => s.Start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday);
        Assert.NotEmpty(weekendSlots);
        Assert.All(weekendSlots, s => Assert.True(
            s.Start.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday));
    }

    [Fact]
    public void Query_reports_free_slots_in_chinese()
    {
        var tomorrow = new DateTime(2026, 11, 19);
        var events = new[] { Event(1, "项目周会", tomorrow.AddHours(10), TimeSpan.FromHours(1)) };

        var result = new AssistantService().Interpret("明天有空吗", events, Now);

        Assert.Equal(AssistantIntent.Query, result.Intent);
        Assert.Contains("空档", result.Message);
        Assert.Contains("11:00", result.Message);
        Assert.Contains("项目周会", result.Message);
        Assert.NotEmpty(result.FreeSlots);
    }

    [Fact]
    public void Moves_event_and_keeps_duration()
    {
        var events = new[]
        {
            Event(1, "项目周会", new DateTime(2026, 11, 18, 10, 0, 0), TimeSpan.FromHours(1))
        };

        var result = new AssistantService().Interpret("把周三的会挪到周四下午", events, Now);

        Assert.Equal(AssistantIntent.Reschedule, result.Intent);
        Assert.True(result.CanApply, result.Message);
        Assert.Equal(1, result.Target!.Id);
        Assert.Equal(new DateTime(2026, 11, 19, 15, 0, 0), result.NewStart);
        Assert.Equal(new DateTime(2026, 11, 19, 16, 0, 0), result.NewEnd);
        Assert.Contains("15:00", result.Message);
    }

    [Fact]
    public void Ambiguous_day_asks_for_a_choice()
    {
        var events = new[]
        {
            Event(1, "项目周会", new DateTime(2026, 11, 18, 10, 0, 0), TimeSpan.FromHours(1)),
            Event(2, "和客户对需求", new DateTime(2026, 11, 18, 14, 30, 0), TimeSpan.FromHours(1))
        };

        var result = new AssistantService().Interpret("把周三的会挪到周四下午", events, Now);

        Assert.True(result.NeedsClarification);
        Assert.Null(result.Target);
        Assert.Equal(2, result.Related.Count);
        Assert.Contains("点一个", result.Message);
    }

    [Fact]
    public void Title_match_wins_over_day()
    {
        var events = new[]
        {
            Event(1, "项目周会", new DateTime(2026, 11, 18, 10, 0, 0), TimeSpan.FromHours(1)),
            Event(2, "和客户对需求", new DateTime(2026, 11, 18, 14, 30, 0), TimeSpan.FromHours(1))
        };

        var result = new AssistantService().Interpret("把和客户对需求改到周五上午", events, Now);

        Assert.True(result.CanApply, result.Message);
        Assert.Equal(2, result.Target!.Id);
        // 说了「上午」但原时间是 14:30：落到上午的默认时间 09:00
        Assert.Equal(new DateTime(2026, 11, 20, 9, 0, 0), result.NewStart);
    }

    [Fact]
    public void Pushes_event_by_duration()
    {
        var events = new[]
        {
            Event(1, "评审", new DateTime(2026, 11, 19, 10, 0, 0), TimeSpan.FromHours(1))
        };

        var result = new AssistantService().Interpret("把明天的评审推迟一小时", events, Now);

        Assert.True(result.CanApply, result.Message);
        Assert.Equal(new DateTime(2026, 11, 19, 11, 0, 0), result.NewStart);
    }

    [Fact]
    public void Reports_conflicts_after_move()
    {
        var events = new[]
        {
            Event(1, "项目周会", new DateTime(2026, 11, 18, 10, 0, 0), TimeSpan.FromHours(1)),
            Event(2, "和客户对需求", new DateTime(2026, 11, 19, 14, 30, 0), TimeSpan.FromHours(1))
        };

        var result = new AssistantService().Interpret("把项目周会挪到明天下午三点", events, Now);

        Assert.True(result.CanApply, result.Message);
        Assert.Single(result.Conflicts);
        Assert.Contains("重叠", result.Message);
    }

    [Fact]
    public void Cancel_finds_event_by_title_and_day()
    {
        var events = new[]
        {
            Event(1, "项目周会", new DateTime(2026, 11, 18, 10, 0, 0), TimeSpan.FromHours(1)),
            Event(2, "和客户对需求", new DateTime(2026, 11, 18, 14, 30, 0), TimeSpan.FromHours(1))
        };

        var result = new AssistantService().Interpret("取消周三的项目周会", events, Now);

        Assert.Equal(AssistantIntent.Cancel, result.Intent);
        Assert.True(result.CanApply, result.Message);
        Assert.Equal(1, result.Target!.Id);
        Assert.Contains("要取消", result.Message);
    }

    [Fact]
    public void Recurring_series_is_flagged_when_moved()
    {
        var events = new[]
        {
            Event(1, "周会", new DateTime(2026, 11, 18, 10, 0, 0), TimeSpan.FromHours(1), rrule: "FREQ=WEEKLY;BYDAY=WE")
        };

        var result = new AssistantService().Interpret("把周三的周会挪到周四下午", events, Now);

        Assert.True(result.CanApply, result.Message);
        Assert.Contains("系列", result.Message);
    }

    [Fact]
    public void Unparseable_move_asks_for_clearer_wording()
    {
        var result = new AssistantService().Interpret("把那个会改一下", Array.Empty<CalendarEvent>(), Now);

        Assert.Equal(AssistantIntent.Reschedule, result.Intent);
        Assert.False(result.CanApply);
        Assert.Contains("没听出", result.Message);
    }

    [Fact]
    public void Create_intent_points_to_ai_card()
    {
        var result = new AssistantService().Interpret("明天下午三点开会", Array.Empty<CalendarEvent>(), Now);

        Assert.Equal(AssistantIntent.Create, result.Intent);
        Assert.Contains("新建", result.Message);
    }
}
