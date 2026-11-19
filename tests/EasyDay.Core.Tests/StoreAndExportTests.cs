using EasyDay.Core.Data;
using EasyDay.Core.Export;
using EasyDay.Core.Models;

namespace EasyDay.Core.Tests;

public class StoreAndExportTests
{
    private static string TempDbPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EasyDayTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "test.db");
    }

    [Fact]
    public void Saves_and_reads_event_with_reminders()
    {
        var dbPath = TempDbPath();
        var store = new EventStore(dbPath);
        store.Initialize();

        var draft = new EventDraft
        {
            Title = "客户拜访",
            Start = DateTime.Now.Date.AddDays(2).AddHours(14),
            End = DateTime.Now.Date.AddDays(2).AddHours(15),
            Location = "国贸"
        };

        var id = store.Add(draft, new[] { 15, 1440 });
        var saved = store.Get(id);

        Assert.NotNull(saved);
        Assert.Equal("客户拜访", saved!.Title);
        Assert.Equal(2, saved.Reminders.Count);
        Assert.All(saved.Reminders, r => Assert.False(r.Fired));
    }

    [Fact]
    public void Editing_event_rebuilds_reminders()
    {
        var dbPath = TempDbPath();
        var store = new EventStore(dbPath);
        store.Initialize();

        var id = store.Add(new EventDraft
        {
            Title = "周会",
            Start = DateTime.Now.Date.AddDays(1).AddHours(9),
            End = DateTime.Now.Date.AddDays(1).AddHours(10)
        }, new[] { 15 });

        var saved = store.Get(id)!;
        saved.Title = "周会（改）";
        store.Save(saved, new[] { 5, 30 });

        var updated = store.Get(id)!;
        Assert.Equal("周会（改）", updated.Title);
        Assert.Equal(2, updated.Reminders.Count);
    }

    [Fact]
    public void Export_produces_valid_ics_lines()
    {
        var item = new CalendarEvent
        {
            Id = 7,
            Title = "项目评审",
            Start = new DateTime(2026, 11, 18, 15, 0, 0),
            End = new DateTime(2026, 11, 18, 16, 0, 0),
            Location = "3 楼会议室",
            RecurrenceRule = "FREQ=WEEKLY;BYDAY=WE"
        };

        var ics = IcsExporter.Build(new[] { item }, new DateTime(2026, 11, 17, 10, 0, 0));

        Assert.Contains("BEGIN:VCALENDAR", ics);
        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("DTSTART:20261118T150000", ics);
        Assert.Contains("RRULE:FREQ=WEEKLY;BYDAY=WE", ics);
        Assert.Contains("LOCATION:3 楼会议室", ics);
    }

    [Fact]
    public void Settings_round_trip()
    {
        var dbPath = TempDbPath();
        var settings = new SettingsService(dbPath);
        settings.Load();
        settings.Current.ApiKey = "test-key";
        settings.Current.WidgetOpacity = 0.9;
        settings.Save();

        var reloaded = new SettingsService(dbPath);
        reloaded.Load();

        Assert.Equal("test-key", reloaded.Current.ApiKey);
        Assert.Equal(0.9, reloaded.Current.WidgetOpacity);
    }
}
