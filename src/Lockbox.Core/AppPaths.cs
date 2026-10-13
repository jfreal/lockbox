using System.Text.Json;

namespace Lockbox.Core;

public static class AppPaths
{
    /// <summary>%LOCALAPPDATA%\Lockbox, or LOCKBOX_DATA_DIR when set (used for testing).</summary>
    public static string DataDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("LOCKBOX_DATA_DIR");
            return string.IsNullOrWhiteSpace(overridden)
                ? Path.Combine(LocalAppData, "Lockbox")
                : overridden;
        }
    }

    public static string VaultPath => Path.Combine(DataDirectory, "vault.lockbox");

    public static string SettingsPath => Path.Combine(DataDirectory, "settings.json");

    /// <summary>A Lockbox folder inside Dropbox, wherever the Dropbox app says it syncs.</summary>
    public static string DefaultBackupFolder =>
        Path.Combine(FindDropboxFolder() ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Dropbox"), "Lockbox");

    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    /// <summary>Reads the Dropbox desktop app's info.json. Personal account first, then business.</summary>
    public static string? FindDropboxFolder()
    {
        foreach (var root in new[] { LocalAppData, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData) })
        {
            var info = Path.Combine(root, "Dropbox", "info.json");
            if (File.Exists(info) && ParseDropboxInfo(File.ReadAllText(info)) is { } path)
                return path;
        }

        return null;
    }

    internal static string? ParseDropboxInfo(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            foreach (var account in new[] { "personal", "business" })
            {
                if (doc.RootElement.TryGetProperty(account, out var entry)
                    && entry.TryGetProperty("path", out var path)
                    && path.GetString() is { Length: > 0 } value)
                    return value;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
