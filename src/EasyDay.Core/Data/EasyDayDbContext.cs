using EasyDay.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace EasyDay.Core.Data;

public sealed class EasyDayDbContext : DbContext
{
    /// <summary>
    /// 数据目录。默认 %APPDATA%\EasyDay；便携模式（exe 旁放 portable.txt）用 exe\data；
    /// 环境变量 EASYDAY_DATA_DIR 优先级最高（调试和绿色版共用一套逻辑）。
    /// </summary>
    public static string DefaultDirectory
    {
        get
        {
            var custom = Environment.GetEnvironmentVariable("EASYDAY_DATA_DIR");
            if (!string.IsNullOrWhiteSpace(custom))
            {
                return custom;
            }

            var portableFlag = Path.Combine(AppContext.BaseDirectory, "portable.txt");
            if (File.Exists(portableFlag))
            {
                return Path.Combine(AppContext.BaseDirectory, "data");
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "EasyDay");
        }
    }

    public static string DefaultPath => Path.Combine(DefaultDirectory, "easyday.db");

    private readonly string _dbPath;

    public EasyDayDbContext(string? dbPath = null)
    {
        _dbPath = dbPath ?? DefaultPath;
    }

    public DbSet<CalendarEvent> Events => Set<CalendarEvent>();

    public DbSet<Reminder> Reminders => Set<Reminder>();

    public DbSet<DraftRecord> Drafts => Set<DraftRecord>();

    public DbSet<Setting> Settings => Set<Setting>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var dir = Path.GetDirectoryName(_dbPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            optionsBuilder.UseSqlite($"Data Source={_dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CalendarEvent>(e =>
        {
            e.HasIndex(x => x.Start);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Location).HasMaxLength(200);
            e.HasMany(x => x.Reminders)
                .WithOne(x => x.Event)
                .HasForeignKey(x => x.CalendarEventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Reminder>(e =>
        {
            e.HasIndex(x => new { x.Fired, x.FireAt });
        });

        modelBuilder.Entity<Setting>(e =>
        {
            e.HasKey(x => x.Key);
        });
    }
}
