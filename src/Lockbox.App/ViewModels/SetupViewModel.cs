using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lockbox.App.Services;
using Lockbox.Core.Vault;
using Microsoft.Win32;

namespace Lockbox.App.ViewModels;

public sealed partial class SetupViewModel(ShellViewModel shell) : ObservableObject
{
    public const int MinimumLength = 16;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrengthLevel), nameof(StrengthText))]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Passphrase { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Confirm { get; set; } = "";

    [ObservableProperty]
    public partial string Error { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    [NotifyPropertyChangedFor(nameof(HelloButtonText))]
    public partial bool IsBusy { get; set; }

    public string HelloButtonText => IsBusy ? "Waiting for Windows Hello\u2026" : "Turn on Windows Hello";

    /// <summary>False on the passphrase step, true on the Windows Hello step.</summary>
    [ObservableProperty]
    public partial bool IsHelloStep { get; set; }

    /// <summary>0 = empty, 1 = too short, 2 = okay, 3 = strong.</summary>
    public int StrengthLevel
    {
        get
        {
            if (Passphrase.Length == 0)
                return 0;
            if (Passphrase.Length < MinimumLength)
                return 1;
            var words = Passphrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return words >= 5 && Passphrase.Length >= 24 ? 3 : 2;
        }
    }

    public string StrengthText => StrengthLevel switch
    {
        0 => "Five or more random words works best, like “orbit maple tunnel crisp lantern”.",
        1 => $"Keep going. At least {MinimumLength} characters.",
        2 => "Good. Five or more random words is even better.",
        _ => "Strong passphrase.",
    };

    private bool CanCreate() => !IsBusy && Passphrase.Length > 0 && Confirm.Length > 0;

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        Error = "";
        if (Passphrase.Length < MinimumLength)
        {
            Error = $"Use at least {MinimumLength} characters.";
            return;
        }

        if (Passphrase != Confirm)
        {
            Error = "The two passphrases don't match.";
            return;
        }

        IsBusy = true;
        try
        {
            var passphrase = Passphrase;
            var vault = await Task.Run(() => shell.Store.Create(passphrase));
            Passphrase = Confirm = "";
            shell.BeginSession(vault, viaPassphrase: true);
            shell.BackUp(announce: false);
            if (shell.HelloAvailable)
                IsHelloStep = true;
            else
                shell.EnterVault();
        }
        catch (Exception ex)
        {
            Error = ErrorText.Of(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task EnableHelloAsync()
    {
        Error = "";
        IsBusy = true;
        try
        {
            await HelloEnrollment.EnableAsync(shell);
            shell.EnterVault();
        }
        catch (HelloCanceledException)
        {
            Error = "Canceled. You can turn on Windows Hello later in Settings.";
        }
        catch (Exception ex)
        {
            Error = ErrorText.Of(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void SkipHello() => shell.EnterVault();

    [RelayCommand]
    private void Restore()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a Lockbox backup",
            Filter = "Lockbox vault (*.lockbox)|*.lockbox",
            InitialDirectory = Directory.Exists(shell.Settings.BackupFolder) ? shell.Settings.BackupFolder : "",
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;

        try
        {
            var bytes = File.ReadAllBytes(dialog.FileName);
            VaultEnvelope.Parse(bytes);
            Directory.CreateDirectory(Path.GetDirectoryName(shell.Store.VaultPath)!);
            File.WriteAllBytes(shell.Store.VaultPath, bytes);
            shell.ShowUnlock(promptForHello: false, "Backup restored. Unlock it with your passphrase.", forcePassphrase: true);
        }
        catch (Exception ex)
        {
            Error = ErrorText.Of(ex);
        }
    }
}

internal static class HelloEnrollment
{
    public static async Task EnableAsync(ShellViewModel shell)
    {
        var keyName = shell.Hello.DefaultKeyName();
        var owner = shell.OwnerWindow;
        await shell.Session.EnableHelloAsync(
            keyName,
            key => shell.Hello.Wrap(key, keyName),
            (name, wrapped) => shell.Hello.UnwrapAsync(wrapped, owner, name));
        shell.BackUp(announce: false);
    }
}
