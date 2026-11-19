using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;

namespace EasyDay.Core.Tests;

public class DuplicateDetectorTests
{
    private static CalendarEvent Existing(
        string title,
        DateTime start,
        int minutes = 60,
        string? rule = null) => new()
    {
        Id = 1,
        Title = title,
        Start = start,
        End = start.AddMinutes(minutes),
        RecurrenceRule = rule
    };

    private static EventDraft Draft(string title, DateTime start, int minutes = 60) => new()
    {
        Title = title,
        Start = start,
        End = start.AddMinutes(minutes)
    };

    [Fact]
    public void Finds_exact_duplicate()
    {
        var existing = new[] { Existing("项目评审会", new DateTime(2026, 11, 19, 15, 0, 0)) };

        var match = DuplicateDetector.Find(Draft("项目评审会", new DateTime(2026, 11, 19, 15, 0, 0)), existing);

        Assert.NotNull(match);
        Assert.Equal("项目评审会", match!.Existing.Title);
    }

    [Fact]
    public void Finds_similar_title_within_five_minutes()
    {
        var existing = new[] { Existing("项目评审会", new DateTime(2026, 11, 19, 15, 0, 0)) };

        var match = DuplicateDetector.Find(Draft("项目评审", new DateTime(2026, 11, 19, 15, 5, 0)), existing);

        Assert.NotNull(match);
    }

    [Fact]
    public void Ignores_different_time()
    {
        var existing = new[] { Existing("项目评审会", new DateTime(2026, 11, 19, 15, 0, 0)) };

        var match = DuplicateDetector.Find(Draft("项目评审会", new DateTime(2026, 11, 19, 16, 0, 0)), existing);

        Assert.Null(match);
    }

    [Fact]
    public void Ignores_different_title_same_time()
    {
        var existing = new[] { Existing("项目评审会", new DateTime(2026, 11, 19, 15, 0, 0)) };

        var match = DuplicateDetector.Find(Draft("客户拜访", new DateTime(2026, 11, 19, 15, 0, 0)), existing);

        Assert.Null(match);
    }

    [Fact]
    public void Finds_duplicate_against_recurring_occurrence()
    {
        var existing = new[]
        {
            Existing("晨会", new DateTime(2026, 11, 16, 9, 0, 0), 30, "FREQ=WEEKLY;BYDAY=MO")
        };

        var match = DuplicateDetector.Find(Draft("晨会", new DateTime(2026, 11, 23, 9, 0, 0), 30), existing);

        Assert.NotNull(match);
        Assert.Equal(new DateTime(2026, 11, 23, 9, 0, 0), match!.Start);
    }

    [Fact]
    public void Ignores_all_day_vs_timed()
    {
        var existing = new[]
        {
            new CalendarEvent
            {
                Id = 2,
                Title = "提交周报",
                Start = new DateTime(2026, 11, 19),
                End = new DateTime(2026, 11, 20),
                AllDay = true
            }
        };

        var match = DuplicateDetector.Find(Draft("提交周报", new DateTime(2026, 11, 19, 9, 0, 0)), existing);

        Assert.Null(match);
    }

    [Theory]
    [InlineData("项目评审会", "项目评审会", true)]
    [InlineData("项目评审", "项目评审会", true)]
    [InlineData("客户拜访", "客户拜访（国贸）", true)]
    [InlineData("周会", "客户拜访", false)]
    public void Similarity_cases(string left, string right, bool expected)
    {
        Assert.Equal(expected, DuplicateDetector.Similar(left, right));
    }
}
