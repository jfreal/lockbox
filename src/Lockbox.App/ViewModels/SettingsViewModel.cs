using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lockbox.App.Services;
using Lockbox.Core;
using Microsoft.Win32;

namespace Lockbox.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public SettingsViewModel(ShellViewModel shell)
    {
        _shell = shell;
        Refresh();
    }

    public bool HelloAvailable => _shell.HelloAvailable;

    [ObservableProperty]
    public partial bool HelloOn { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HelloButtonText))]
    public partial bool IsBusy { get; set; }

    public string HelloDetail => !HelloAvailable
        ? "Not available on this PC. Set up a PIN or face/fingerprint in Windows Settings first."
        : HelloOn ? "Unlock with your PIN, face, or fingerprint." : "Off. You unlock with your passphrase.";

    public string HelloButtonText => IsBusy ? "Waiting\u2026" : HelloOn ? "Turn off" : "Turn on";

    public string PassphraseDetail => _shell.Settings.LastPassphraseUnlock is { } last
        ? $"Last typed {Friendly(last)}. Lockbox asks for it every two weeks so you don't forget it."
        : "Lockbox asks for it every two weeks so you don't forget it.";

    public string AutoLockDetail => $"Locks after {AppSettings.AutoLockAfter.TotalMinutes:0} minutes without activity, and whenever you lock Windows.";

    public string BackupFolder => _shell.Settings.BackupFolder;

    public string LastBackupDetail => _shell.Settings.LastBackup is { } last ? $"Last backup {Friendly(last)}." : "No backup yet.";

    public string VaultPath => _shell.Store.VaultPath;

    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";

    private void Refresh()
    {
        HelloOn = _shell.IsUnlocked && _shell.Session.HasHelloForThisDevice;
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private async Task ToggleHelloAsync()
    {
        IsBusy = true;
        try
        {
            if (HelloOn)
            {
                _shell.Session.DisableHello();
                _shell.BackUp(announce: false);
                _shell.ShowToast("Windows Hello turned off");
            }
            else
            {
                await HelloEnrollment.EnableAsync(_shell);
                _shell.ShowToast("Windows Hello turned on");
            }
        }
        catch (HelloCanceledException)
        {
        }
        catch (Exception ex)
        {
            _shell.ShowToast(ErrorText.Of(ex), isLong: true);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    [RelayCommand]
    private void ChangePassphrase() => _shell.OpenSheet(new ChangePassphraseSheet(_shell, Refresh));

    [RelayCommand]
    private void ChooseBackupFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Choose where Lockbox keeps backups",
            InitialDirectory = Directory.Exists(BackupFolder) ? BackupFolder : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;

        // Keep backups in their own subfolder so pruning never touches anything else.
        var chosen = dialog.FolderName;
        _shell.Settings.BackupFolder = string.Equals(Path.GetFileName(chosen), "Lockbox", StringComparison.OrdinalIgnoreCase)
            ? chosen
            : Path.Combine(chosen, "Lockbox");
        _shell.SaveSettings();
        _shell.BackUp(announce: true);
        Refresh();
    }

    [RelayCommand]
    private void BackUpNow()
    {
        _shell.BackUp(announce: true);
        Refresh();
    }

    [RelayCommand]
    private void ShowBackupFolder()
    {
        if (Directory.Exists(BackupFolder))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{BackupFolder}\"") { UseShellExecute = true });
        else
            _shell.ShowToast("The backup folder doesn't exist yet.");
    }

    [RelayCommand]
    private void ShowVaultFile() =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{VaultPath}\"") { UseShellExecute = true });

    [RelayCommand]
    private void Back() => _shell.ShowVault();

    private static string Friendly(DateTimeOffset when)
    {
        var local = when.ToLocalTime();
        var today = DateTime.Today;
        if (local.Date == today)
            return $"today at {local:h:mm tt}";
        if (local.Date == today.AddDays(-1))
            return $"yesterday at {local:h:mm tt}";
        return $"on {local:MMM d}";
    }
}
