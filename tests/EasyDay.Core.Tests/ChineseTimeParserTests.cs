using EasyDay.Core.Parsing;

namespace EasyDay.Core.Tests;

public class ChineseTimeParserTests
{
    // 2026-11-17 是星期二
    private static readonly DateTime Now = new(2026, 11, 17, 10, 0, 0);

    [Fact]
    public void Parses_relative_day_time_location_and_reminder()
    {
        var result = ChineseTimeParser.Parse(
            "明天下午3点在3楼会议室开项目评审会，提前15分钟提醒",
            Now);

        Assert.Equal(new DateOnly(2026, 11, 18), result.Date);
        Assert.Equal(new TimeOnly(15, 0), result.Start);
        Assert.Equal("3楼会议室", result.Location);
        Assert.Equal(15, result.ReminderMinutes);
        Assert.Contains("项目评审", result.Title);
    }

    [Fact]
    public void Parses_next_week_and_time_range()
    {
        var result = ChineseTimeParser.Parse("下周三上午10点到11点半，302会议室开会", Now);

        Assert.Equal(new DateOnly(2026, 11, 25), result.Date);
        Assert.Equal(new TimeOnly(10, 0), result.Start);
        Assert.Equal(new TimeOnly(11, 30), result.End);
    }

    [Fact]
    public void Parses_absolute_date_and_range()
    {
        var result = ChineseTimeParser.Parse("11月20日 14:00-15:30 客户拜访", Now);

        Assert.Equal(new DateOnly(2026, 11, 20), result.Date);
        Assert.Equal(new TimeOnly(14, 0), result.Start);
        Assert.Equal(new TimeOnly(15, 30), result.End);
        Assert.Equal("客户拜访", result.Title);
    }

    [Fact]
    public void Parses_tomorrow_without_time_as_allday_candidate()
    {
        var result = ChineseTimeParser.Parse("后天提交项目周报", Now);

        Assert.Equal(new DateOnly(2026, 11, 19), result.Date);
        Assert.Null(result.Start);
    }

    [Fact]
    public void Parses_pm_range_inheriting_period()
    {
        var result = ChineseTimeParser.Parse("明天下午3点到5点讨论方案", Now);

        Assert.Equal(new TimeOnly(15, 0), result.Start);
        Assert.Equal(new TimeOnly(17, 0), result.End);
    }

    [Fact]
    public void Parses_duration_in_hours()
    {
        var result = ChineseTimeParser.Parse("明天上午9点开会，时长2小时", Now);

        Assert.Equal(new TimeOnly(9, 0), result.Start);
        Assert.Equal(new TimeOnly(11, 0), result.End);
    }

    [Fact]
    public void Falls_back_to_local_draft()
    {
        var draft = LocalDraftFactory.Build("明天下午3点开会", Now);

        Assert.False(draft.AllDay);
        Assert.Equal(new DateTime(2026, 11, 18, 15, 0, 0), draft.Start);
        Assert.Equal(new DateTime(2026, 11, 18, 16, 0, 0), draft.End);
        Assert.InRange(draft.Confidence, 0.3, 0.95);
    }
}
