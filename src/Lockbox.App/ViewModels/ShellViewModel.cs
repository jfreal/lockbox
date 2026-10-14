using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Lockbox.App.Services;
using Lockbox.Core;
using Lockbox.Core.Vault;

namespace Lockbox.App.ViewModels;

/// <summary>Owns the open vault, the current screen, the sheet on top of it, and the toast.</summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly DispatcherTimer _toastTimer = new();
    private UnlockedVault? _vault;
    private DateTime _lastActivityUtc = DateTime.UtcNow;

    public ShellViewModel(VaultStore store, AppSettings settings, string settingsPath, WindowsHelloService hello, SecureClipboard clipboard)
    {
        Store = store;
        Settings = settings;
        SettingsPath = settingsPath;
        Hello = hello;
        Clipboard = clipboard;
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            Toast = null;
        };
    }

    public VaultStore Store { get; }
    public AppSettings Settings { get; }
    public string SettingsPath { get; }
    public WindowsHelloService Hello { get; }
    public SecureClipboard Clipboard { get; }

    public bool HelloAvailable { get; set; }

    /// <summary>The window Windows Hello's prompt should sit on top of.</summary>
    public nint OwnerWindow => Application.Current?.MainWindow is { } window ? new WindowInteropHelper(window).Handle : 0;

    [ObservableProperty]
    public partial object? CurrentScreen { get; set; }

    [ObservableProperty]
    public partial ISheet? Sheet { get; set; }

    [ObservableProperty]
    public partial string? Toast { get; set; }

    public VaultViewModel? Vault { get; private set; }

    public UnlockedVault Session => _vault ?? throw new InvalidOperationException("The vault is locked.");

    public bool IsUnlocked => _vault is not null;

    public async Task StartAsync(bool promptForHello = true)
    {
        HelloAvailable = await Hello.IsAvailableAsync();
        if (Store.Exists)
            ShowUnlock(promptForHello);
        else
            CurrentScreen = new SetupViewModel(this);
    }

    public void ShowUnlock(bool promptForHello, string? message = null, bool forcePassphrase = false) =>
        CurrentScreen = new UnlockViewModel(this, promptForHello, message, forcePassphrase);

    /// <summary>Holds the vault open without leaving the current screen (setup's Windows Hello step).</summary>
    public void BeginSession(UnlockedVault vault, bool viaPassphrase)
    {
        _vault?.Dispose();
        _vault = vault;
        Touch();
        if (viaPassphrase)
        {
            Settings.LastPassphraseUnlock = DateTimeOffset.UtcNow;
            SaveSettings();
        }
    }

    public void EnterVault()
    {
        Vault = new VaultViewModel(this, Session.Data);
        CurrentScreen = Vault;
    }

    public void ShowVault()
    {
        if (Vault is not null)
            CurrentScreen = Vault;
    }

    public void Lock()
    {
        if (_vault is null)
            return;

        Vault?.CloseNote();
        Sheet = null;
        Clipboard.ClearIfOurs();
        _vault.Dispose();
        _vault = null;
        Vault = null;
        ShowUnlock(promptForHello: false);
    }

    /// <summary>Saves the vault, then copies it to the backup folder. Returns false if the save failed.</summary>
    public bool Save()
    {
        try
        {
            Session.Save();
        }
        catch (Exception ex)
        {
            ShowToast($"Couldn't save: {ErrorText.Of(ex)}", isLong: true);
            return false;
        }

        BackUp(announce: false);
        return true;
    }

    public bool BackUp(bool announce)
    {
        var folder = Settings.BackupFolder;
        var parent = Path.GetDirectoryName(folder);
        if (parent is null || !Directory.Exists(parent))
        {
            ShowToast($"Saved on this PC. Backup skipped: {parent} doesn't exist.", isLong: true);
            return false;
        }

        try
        {
            new BackupService(folder).BackUp(Store.VaultPath, DateTimeOffset.Now);
            Settings.LastBackup = DateTimeOffset.UtcNow;
            SaveSettings();
            if (announce)
                ShowToast("Backed up");
            return true;
        }
        catch (Exception ex)
        {
            ShowToast($"Saved on this PC. Backup failed: {ErrorText.Of(ex)}", isLong: true);
            return false;
        }
    }

    public void SaveSettings()
    {
        try
        {
            Settings.Save(SettingsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Settings are conveniences; the vault itself is unaffected.
        }
    }

    public void ShowToast(string text, bool isLong = false)
    {
        Toast = text;
        _toastTimer.Stop();
        _toastTimer.Interval = TimeSpan.FromSeconds(isLong ? 6 : 2.4);
        _toastTimer.Start();
    }

    public void OpenSheet(ISheet sheet) => Sheet = sheet;

    public void CloseSheet() => Sheet = null;

    public void Touch() => _lastActivityUtc = DateTime.UtcNow;

    public void LockIfIdle()
    {
        if (IsUnlocked && DateTime.UtcNow - _lastActivityUtc >= AppSettings.AutoLockAfter)
            Lock();
    }
}

/// <summary>A modal card shown over the current screen.</summary>
public interface ISheet
{
    void Cancel();
}
