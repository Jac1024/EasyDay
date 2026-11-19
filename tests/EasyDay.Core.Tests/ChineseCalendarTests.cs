using EasyDay.Core.Calendar;

namespace EasyDay.Core.Tests;

public class ChineseCalendarTests
{
    [Fact]
    public void Returns_lunar_date_for_regular_day()
    {
        var info = ChineseCalendarService.Get(new DateOnly(2026, 11, 17));

        Assert.Equal("十月初九", info.LunarMonthDay);
        Assert.Equal("初九", info.LunarDay);
        Assert.True(info.IsWorkday);
        Assert.False(info.IsStatutoryHoliday);
        Assert.Equal(47, info.WeekOfYear);
    }

    [Fact]
    public void Detects_solar_term_light_snow()
    {
        var info = ChineseCalendarService.Get(new DateOnly(2026, 11, 22));

        Assert.Equal("小雪", info.SolarTerm);
        Assert.Equal(SubTextKind.SolarTerm, info.SubTextKind);
    }

    [Fact]
    public void Detects_solar_term_start_of_winter()
    {
        var info = ChineseCalendarService.Get(new DateOnly(2026, 11, 7));

        Assert.Equal("立冬", info.SolarTerm);
    }

    [Fact]
    public void Marks_national_day_as_holiday()
    {
        var info = ChineseCalendarService.Get(new DateOnly(2026, 10, 1));

        Assert.True(info.IsStatutoryHoliday);
        Assert.False(info.IsWorkday);
        Assert.Equal("国庆", info.HolidayName);
        Assert.Equal("休", info.BadgeText);
    }

    [Fact]
    public void Marks_makeup_saturday_as_workday()
    {
        var info = ChineseCalendarService.Get(new DateOnly(2026, 10, 10));

        Assert.True(info.IsMakeupWorkday);
        Assert.True(info.IsWorkday);
        Assert.Equal("班", info.BadgeText);
    }

    [Fact]
    public void Weekend_is_not_workday()
    {
        var info = ChineseCalendarService.Get(new DateOnly(2026, 11, 21));

        Assert.False(info.IsWorkday);
        Assert.False(info.IsStatutoryHoliday);
    }

    [Fact]
    public void Detects_spring_festival()
    {
        var info = ChineseCalendarService.Get(new DateOnly(2026, 2, 17));

        Assert.Equal("春节", info.Festival);
        Assert.Equal(SubTextKind.Festival, info.SubTextKind);
        Assert.True(info.IsStatutoryHoliday);
    }

    [Fact]
    public void Detects_lunar_new_years_eve()
    {
        var info = ChineseCalendarService.Get(new DateOnly(2026, 2, 16));

        Assert.Equal("除夕", info.Festival);
    }
}
