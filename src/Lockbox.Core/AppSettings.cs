using System.Text.Json;

namespace Lockbox.Core;

/// <summary>Plain, non-secret preferences stored next to the vault.</summary>
public sealed class AppSettings
{
    public static readonly TimeSpan AutoLockAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan PassphraseCheckInterval = TimeSpan.FromDays(14);

    public string BackupFolder { get; set; } = AppPaths.DefaultBackupFolder;
    public DateTimeOffset? LastPassphraseUnlock { get; set; }
    public DateTimeOffset? LastBackup { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public bool PassphraseCheckDue(DateTimeOffset now) =>
        LastPassphraseUnlock is null || now - LastPassphraseUnlock.Value >= PassphraseCheckInterval;

    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllBytes(path), JsonOptions) ?? new AppSettings()
                : new AppSettings();
        }
        catch (JsonException)
        {
            return new AppSettings();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions));
    }
}
