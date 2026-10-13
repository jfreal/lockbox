namespace Lockbox.Core.Vault;

/// <summary>
/// Copies the (already encrypted) vault file to the backup folder: a "latest" copy replaced on
/// every save, plus one snapshot per day, keeping the newest <see cref="DailySnapshotsToKeep"/>.
/// </summary>
public sealed class BackupService(string backupFolder)
{
    public const int DailySnapshotsToKeep = 10;
    public const string LatestFileName = "vault.lockbox";
    public const string HistoryFolderName = "history";

    public string BackupFolder { get; } = backupFolder;

    public void BackUp(string vaultPath, DateTimeOffset now)
    {
        var bytes = File.ReadAllBytes(vaultPath);
        Directory.CreateDirectory(BackupFolder);
        FileUtil.WriteAtomically(Path.Combine(BackupFolder, LatestFileName), bytes);

        var history = Path.Combine(BackupFolder, HistoryFolderName);
        Directory.CreateDirectory(history);
        FileUtil.WriteAtomically(Path.Combine(history, $"vault-{now.ToLocalTime():yyyy-MM-dd}.lockbox"), bytes);

        var expired = Directory.GetFiles(history, "vault-*.lockbox")
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(DailySnapshotsToKeep);
        foreach (var file in expired)
            File.Delete(file);
    }
}
