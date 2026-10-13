using Lockbox.Core.Vault;

namespace Lockbox.Tests;

public sealed class BackupServiceTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lockbox-backup-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Writes_latest_copy_and_keeps_ten_daily_snapshots()
    {
        var vault = Path.Combine(_dir, "vault.lockbox");
        var backups = Path.Combine(_dir, "Dropbox", "Lockbox");
        var service = new BackupService(backups);
        var start = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        for (var day = 0; day < 14; day++)
        {
            File.WriteAllText(vault, $"day {day}");
            service.BackUp(vault, start.AddDays(day));
            service.BackUp(vault, start.AddDays(day).AddHours(1)); // same day: overwrites, no extra file
        }

        Assert.Equal("day 13", File.ReadAllText(Path.Combine(backups, BackupService.LatestFileName)));
        var history = Directory.GetFiles(Path.Combine(backups, BackupService.HistoryFolderName)).Select(Path.GetFileName).Order().ToList();
        Assert.Equal(BackupService.DailySnapshotsToKeep, history.Count);
        Assert.Equal($"vault-{start.AddDays(4).ToLocalTime():yyyy-MM-dd}.lockbox", history[0]);
        Assert.Empty(Directory.GetFiles(backups, "*.tmp", SearchOption.AllDirectories));
    }
}
