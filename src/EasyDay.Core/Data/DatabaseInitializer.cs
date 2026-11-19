using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EasyDay.Core.Data;

/// <summary>数据库初始化结果，用于启动日志。</summary>
public sealed record DatabaseInitResult(
    string Path,
    bool Existed,
    bool Recreated,
    int PreviousVersion,
    int Version,
    string? BackupPath);

/// <summary>
/// 数据库初始化。公开发布后不能再只依赖 EnsureCreated：老版本库缺列会直接崩。
/// 这里用 SQLite 的 <c>user_version</c> 记录结构版本，升级前自动备份，损坏库自动重建。
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>
    /// 当前结构版本。以后新增字段/索引时 +1，并在 <see cref="ApplyMigrations"/> 里补一步升级。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    public static DatabaseInitResult EnsureReady(string? dbPath = null)
    {
        var path = dbPath ?? EasyDayDbContext.DefaultPath;
        var existed = File.Exists(path);
        var previous = existed ? ReadUserVersion(path) : 0;
        string? backup = null;

        if (existed && previous > 0 && previous < CurrentSchemaVersion)
        {
            backup = Backup(path);
        }

        try
        {
            using var db = new EasyDayDbContext(path);
            db.Database.EnsureCreated();

            if (previous < CurrentSchemaVersion)
            {
                ApplyMigrations(db);
                db.Database.ExecuteSqlRaw($"PRAGMA user_version = {CurrentSchemaVersion}");
            }

            return new DatabaseInitResult(path, existed, false, previous, CurrentSchemaVersion, backup);
        }
        catch (Exception) when (existed)
        {
            // 库损坏（断电、磁盘异常、手工改坏）时不要卡在启动失败：备份后重建一个空库。
            backup ??= Backup(path);
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            previous = 0;

            using var db = new EasyDayDbContext(path);
            db.Database.EnsureCreated();
            db.Database.ExecuteSqlRaw($"PRAGMA user_version = {CurrentSchemaVersion}");

            return new DatabaseInitResult(path, true, true, 0, CurrentSchemaVersion, backup);
        }
    }

    /// <summary>
    /// 按版本号补齐结构。首个公开发布版（v1）由 EnsureCreated 建出完整结构，无需额外语句。
    /// 以后示例：
    /// <code>
    /// if (from &lt; 2) db.Database.ExecuteSqlRaw("ALTER TABLE Events ADD COLUMN ...");
    /// </code>
    /// </summary>
    private static void ApplyMigrations(EasyDayDbContext db)
    {
    }

    private static int ReadUserVersion(string path)
    {
        try
        {
            using var connection = new SqliteConnection($"Data Source={path}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version";
            return Convert.ToInt32(command.ExecuteScalar() ?? 0);
        }
        catch
        {
            return 0;
        }
    }

    private static string Backup(string path)
    {
        var target = $"{path}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
        try
        {
            File.Copy(path, target, true);
        }
        catch
        {
            // 备份失败不应该阻止启动
        }

        return target;
    }
}
