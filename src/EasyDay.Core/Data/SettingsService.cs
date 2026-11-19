using EasyDay.Core.Models;
using EasyDay.Core.Security;

namespace EasyDay.Core.Data;

/// <summary>
/// 设置读写。整个 AppSettings 序列化成一行 JSON，方便扩展。
/// API Key 落盘前用 Windows DPAPI 按当前用户加密。
/// </summary>
public sealed class SettingsService
{
    private const string Key = "AppSettings";
    private readonly string? _dbPath;

    public SettingsService(string? dbPath = null)
    {
        _dbPath = dbPath;
    }

    public AppSettings Current { get; set; } = new();

    public void Load()
    {
        DatabaseInitializer.EnsureReady(_dbPath);
        using var db = new EasyDayDbContext(_dbPath);
        var row = db.Settings.FirstOrDefault(x => x.Key == Key);
        if (row is null || string.IsNullOrWhiteSpace(row.Value))
        {
            Current = new AppSettings();
            return;
        }

        try
        {
            Current = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(row.Value) ?? new AppSettings();

            // 早期版本明文保存，读到密文则解密；明文则原样使用并在下次保存时自动加密
            var storedKey = Current.ApiKey;
            Current.ApiKey = SecretProtector.Unprotect(storedKey);

            if (storedKey.Length > 0 && Current.ApiKey == storedKey && OperatingSystem.IsWindows())
            {
                Save();
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        DatabaseInitializer.EnsureReady(_dbPath);
        using var db = new EasyDayDbContext(_dbPath);

        // API Key 落盘前用 DPAPI 加密，内存里始终保持明文供请求使用
        var snapshot = Current.Clone();
        snapshot.ApiKey = SecretProtector.Protect(Current.ApiKey);

        var json = System.Text.Json.JsonSerializer.Serialize(snapshot);
        var row = db.Settings.FirstOrDefault(x => x.Key == Key);
        if (row is null)
        {
            db.Settings.Add(new Setting { Key = Key, Value = json });
        }
        else
        {
            row.Value = json;
        }

        db.SaveChanges();
    }
}
