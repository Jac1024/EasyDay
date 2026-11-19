using EasyDay.Core.Data;
using EasyDay.Core.Models;
using EasyDay.Core.Security;

namespace EasyDay.Core.Tests;

public class ReleaseHardeningTests
{
    private static string TempDbPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "EasyDayTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "test.db");
    }

    private static string ReadStoredJson(string dbPath)
    {
        using var db = new EasyDayDbContext(dbPath);
        return db.Settings.Single(x => x.Key == "AppSettings").Value;
    }

    [Fact]
    public void New_settings_have_no_provider_or_key()
    {
        var settings = new AppSettings();

        Assert.Empty(settings.ApiBaseUrl);
        Assert.Empty(settings.ApiKey);
        Assert.Empty(settings.Model);
        Assert.False(settings.HasAiKey);
        Assert.False(settings.HasAiConfig);
    }

    [Fact]
    public void Secret_round_trips_and_is_idempotent()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        const string secret = "sk-test-1234567890-中文";

        var stored = SecretProtector.Protect(secret);
        Assert.StartsWith("dpapi:v1:", stored);
        Assert.DoesNotContain(secret, stored);
        Assert.Equal(stored, SecretProtector.Protect(stored));
        Assert.Equal(secret, SecretProtector.Unprotect(stored));
    }

    [Fact]
    public void Legacy_plaintext_key_is_still_readable()
    {
        Assert.Equal("sk-legacy", SecretProtector.Unprotect("sk-legacy"));
        Assert.Equal(string.Empty, SecretProtector.Unprotect(string.Empty));
        Assert.Equal(string.Empty, SecretProtector.Unprotect(null));
        Assert.Equal(string.Empty, SecretProtector.Unprotect("dpapi:v1:not-base64"));
    }

    [Fact]
    public void Settings_save_encrypts_api_key_and_load_decrypts_it()
    {
        var dbPath = TempDbPath();
        var settings = new SettingsService(dbPath);
        settings.Load();
        settings.Current.ApiKey = "sk-very-secret-value";
        settings.Current.Model = "demo-model";
        settings.Save();

        var stored = ReadStoredJson(dbPath);
        Assert.DoesNotContain("sk-very-secret-value", stored);

        var reloaded = new SettingsService(dbPath);
        reloaded.Load();
        Assert.Equal("sk-very-secret-value", reloaded.Current.ApiKey);
        Assert.Equal("demo-model", reloaded.Current.Model);
        Assert.True(reloaded.Current.HasAiKey);
    }

    [Fact]
    public void Loading_legacy_plaintext_settings_upgrades_to_encrypted()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var dbPath = TempDbPath();
        using (var db = new EasyDayDbContext(dbPath))
        {
            db.Database.EnsureCreated();
            db.Settings.Add(new Setting
            {
                Key = "AppSettings",
                Value = "{\"ApiKey\":\"sk-legacy-1\",\"Model\":\"legacy-model\"}"
            });
            db.SaveChanges();
        }

        var settings = new SettingsService(dbPath);
        settings.Load();

        Assert.Equal("sk-legacy-1", settings.Current.ApiKey);
        Assert.Equal("legacy-model", settings.Current.Model);
        Assert.DoesNotContain("sk-legacy-1", ReadStoredJson(dbPath));
    }

    [Fact]
    public void Database_initializer_sets_schema_version_once()
    {
        var dbPath = TempDbPath();

        var first = DatabaseInitializer.EnsureReady(dbPath);
        Assert.False(first.Existed);
        Assert.False(first.Recreated);
        Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, first.Version);
        Assert.Null(first.BackupPath);

        var second = DatabaseInitializer.EnsureReady(dbPath);
        Assert.True(second.Existed);
        Assert.False(second.Recreated);
        Assert.Equal(DatabaseInitializer.CurrentSchemaVersion, second.PreviousVersion);
        Assert.Null(second.BackupPath);
    }

    [Fact]
    public void Database_initializer_recovers_from_corrupt_file()
    {
        var dbPath = TempDbPath();
        File.WriteAllText(dbPath, "this is not a sqlite database");

        var result = DatabaseInitializer.EnsureReady(dbPath);

        Assert.True(result.Recreated);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));

        var store = new EventStore(dbPath);
        var id = store.Add(new EventDraft
        {
            Title = "恢复后仍可用",
            Start = DateTime.Now.Date.AddHours(9),
            End = DateTime.Now.Date.AddHours(10)
        }, new[] { 15 });

        Assert.NotNull(store.Get(id));
    }
}
