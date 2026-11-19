using System.IO;
using EasyDay.Core.Data;

namespace EasyDay.App.Infrastructure;

public static class FileLog
{
    private static readonly object Gate = new();

    private static string LogPath => Path.Combine(EasyDayDbContext.DefaultDirectory, "logs", "app.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                var path = LogPath;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                // 常驻程序会跑很久，日志超过 1MB 就轮转一份，避免无限增长
                var info = new FileInfo(path);
                if (info.Exists && info.Length > 1_000_000)
                {
                    var archive = path + ".1";
                    File.Delete(archive);
                    File.Move(path, archive);
                }

                File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {level} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响主流程
        }
    }
}
