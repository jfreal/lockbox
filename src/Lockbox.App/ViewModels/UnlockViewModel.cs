using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lockbox.App.Services;
using Lockbox.Core.Vault;

namespace Lockbox.App.ViewModels;

public sealed partial class UnlockViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;

    public UnlockViewModel(ShellViewModel shell, bool promptForHello, string? message, bool forcePassphrase)
    {
        _shell = shell;

        var helloEnrolled = SafeHasHello(shell);
        var checkDue = helloEnrolled && shell.Settings.PassphraseCheckDue(DateTimeOffset.UtcNow);
        CanUseHello = shell.HelloAvailable && helloEnrolled && !checkDue && !forcePassphrase;
        UsePassphrase = !CanUseHello;
        Subtitle = message
            ?? (checkDue
                ? "It's been two weeks. Type your passphrase this time so you don't forget it."
                : "Unlock to see your secrets.");

        if (promptForHello && CanUseHello)
            Application.Current.Dispatcher.BeginInvoke(() => UnlockWithHelloCommand.Execute(null), DispatcherPriority.ApplicationIdle);
    }

    public bool CanUseHello { get; }

    public string Subtitle { get; }

    [ObservableProperty]
    public partial bool UsePassphrase { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockWithPassphraseCommand))]
    public partial string Passphrase { get; set; } = "";

    [ObservableProperty]
    public partial string Error { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UnlockWithPassphraseCommand), nameof(UnlockWithHelloCommand))]
    [NotifyPropertyChangedFor(nameof(HelloButtonText))]
    public partial bool IsBusy { get; set; }

    public string HelloButtonText => IsBusy ? "Waiting for Windows Hello\u2026" : "Unlock with Windows Hello";

    private bool CanUnlockWithPassphrase() => !IsBusy && Passphrase.Length > 0;

    [RelayCommand(CanExecute = nameof(CanUnlockWithPassphrase))]
    private async Task UnlockWithPassphraseAsync()
    {
        Error = "";
        IsBusy = true;
        try
        {
            var passphrase = Passphrase;
            var vault = await Task.Run(() => _shell.Store.UnlockWithPassphrase(passphrase));
            Passphrase = "";
            _shell.BeginSession(vault, viaPassphrase: true);
            _shell.EnterVault();
        }
        catch (WrongPassphraseException ex)
        {
            Passphrase = "";
            Error = ErrorText.Of(ex);
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

    private bool CanUnlockWithHello() => !IsBusy && CanUseHello;

    [RelayCommand(CanExecute = nameof(CanUnlockWithHello))]
    private async Task UnlockWithHelloAsync()
    {
        Error = "";
        IsBusy = true;
        try
        {
            var owner = _shell.OwnerWindow;
            var vault = await _shell.Store.UnlockWithHelloAsync((keyName, wrapped) => _shell.Hello.UnwrapAsync(wrapped, owner, keyName));
            _shell.BeginSession(vault, viaPassphrase: false);
            _shell.EnterVault();
        }
        catch (HelloCanceledException)
        {
            // Stay on this screen; the button is right there.
        }
        catch (Exception ex)
        {
            Error = ErrorText.Of(ex);
            UsePassphrase = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void TogglePassphrase()
    {
        Error = "";
        UsePassphrase = !UsePassphrase;
    }

    private static bool SafeHasHello(ShellViewModel shell)
    {
        try
        {
            return shell.Store.HasHelloForThisDevice();
        }
        catch (InvalidVaultFileException)
        {
            return false;
        }
    }
}
