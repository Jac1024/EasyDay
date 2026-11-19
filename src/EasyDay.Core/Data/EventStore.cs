using EasyDay.Core.Models;
using EasyDay.Core.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace EasyDay.Core.Data;

/// <summary>所有日程读写都走这里。数据量不大，采用"全量加载 + 内存展开重复"策略。</summary>
public sealed class EventStore
{
    private readonly string? _dbPath;

    public EventStore(string? dbPath = null)
    {
        _dbPath = dbPath;
    }

    private EasyDayDbContext CreateDb() => new(_dbPath);

    public DatabaseInitResult Initialize() => DatabaseInitializer.EnsureReady(_dbPath);

    public List<CalendarEvent> GetAll()
    {
        using var db = CreateDb();
        return db.Events
            .AsNoTracking()
            .Include(e => e.Reminders)
            .OrderBy(e => e.Start)
            .ToList();
    }

    public CalendarEvent? Get(int id)
    {
        using var db = CreateDb();
        return db.Events
            .AsNoTracking()
            .Include(e => e.Reminders)
            .FirstOrDefault(e => e.Id == id);
    }

    public int Save(CalendarEvent input, IReadOnlyCollection<int> reminderOffsets)
    {
        using var db = CreateDb();
        CalendarEvent entity;

        if (input.Id == 0)
        {
            entity = new CalendarEvent();
            db.Events.Add(entity);
        }
        else
        {
            entity = db.Events.Include(e => e.Reminders).First(e => e.Id == input.Id);
            db.Reminders.RemoveRange(entity.Reminders);
            entity.Reminders.Clear();
        }

        entity.Title = input.Title.Trim();
        entity.Start = input.Start;
        entity.End = input.End;
        entity.AllDay = input.AllDay;
        entity.Location = input.Location;
        entity.Notes = input.Notes;
        entity.Source = input.Source;
        entity.RawText = input.RawText;
        entity.Confidence = input.Confidence;
        entity.Status = input.Status;
        entity.RecurrenceRule = input.RecurrenceRule;

        foreach (var reminder in ReminderPlanner.Build(entity.Start, entity.AllDay, reminderOffsets, DateTime.Now))
        {
            entity.Reminders.Add(reminder);
        }

        db.SaveChanges();
        return entity.Id;
    }

    public int Add(EventDraft draft, IReadOnlyCollection<int>? reminderOffsets = null)
    {
        var offsets = reminderOffsets is { Count: > 0 }
            ? reminderOffsets
            : draft.ReminderMinutes.Count > 0
                ? draft.ReminderMinutes
                : ReminderPlanner.Suggest(draft);

        return Save(draft.ToEvent(), offsets);
    }

    public void Delete(int id)
    {
        using var db = CreateDb();
        var entity = db.Events.Include(e => e.Reminders).FirstOrDefault(e => e.Id == id);
        if (entity is null)
        {
            return;
        }

        db.Events.Remove(entity);
        db.SaveChanges();
    }

    public void SetStatus(int id, EventStatus status)
    {
        using var db = CreateDb();
        var entity = db.Events.FirstOrDefault(e => e.Id == id);
        if (entity is null)
        {
            return;
        }

        entity.Status = status;
        db.SaveChanges();
    }

    public List<Reminder> GetDueReminders(DateTime now)
    {
        using var db = CreateDb();
        return db.Reminders
            .AsNoTracking()
            .Include(r => r.Event)
            .Where(r => !r.Fired && r.FireAt <= now)
            .Where(r => r.Event != null && r.Event.Status == EventStatus.Active)
            .OrderBy(r => r.FireAt)
            .ToList();
    }

    public void MarkFired(int reminderId)
    {
        using var db = CreateDb();
        var reminder = db.Reminders.FirstOrDefault(r => r.Id == reminderId);
        if (reminder is null)
        {
            return;
        }

        reminder.Fired = true;
        db.SaveChanges();
    }

    public int Snooze(int reminderId, int minutes)
    {
        using var db = CreateDb();
        var reminder = db.Reminders.FirstOrDefault(r => r.Id == reminderId);
        if (reminder is null)
        {
            return 0;
        }

        reminder.Fired = true;
        var next = new Reminder
        {
            CalendarEventId = reminder.CalendarEventId,
            FireAt = DateTime.Now.AddMinutes(minutes),
            SnoozeCount = reminder.SnoozeCount + 1
        };
        db.Reminders.Add(next);
        db.SaveChanges();
        return next.Id;
    }

    public void RecordDraft(DraftRecord record)
    {
        using var db = CreateDb();
        db.Drafts.Add(record);
        db.SaveChanges();
    }

    public List<DraftRecord> RecentDrafts(int take = 30)
    {
        using var db = CreateDb();
        return db.Drafts
            .AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .Take(take)
            .ToList();
    }
}
