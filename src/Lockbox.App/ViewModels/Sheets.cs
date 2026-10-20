using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lockbox.Core.Vault;

namespace Lockbox.App.ViewModels;

public sealed partial class TextPromptSheet(ShellViewModel shell) : ObservableObject, ISheet
{
    public string Title { get; init; } = "";
    public string? Message { get; init; }
    public string Placeholder { get; init; } = "";
    public string ConfirmText { get; init; } = "OK";
    public Func<string, string?> Validate { get; init; } = _ => null;
    public Action<string> OnConfirm { get; init; } = _ => { };

    [ObservableProperty]
    public partial string Text { get; set; } = "";

    [ObservableProperty]
    public partial string Error { get; set; } = "";

    [RelayCommand]
    private void Confirm()
    {
        var text = Text.Trim();
        var error = Validate(text);
        if (error is not null)
        {
            Error = error;
            return;
        }

        shell.CloseSheet();
        OnConfirm(text);
    }

    [RelayCommand]
    public void Cancel() => shell.CloseSheet();
}

public sealed partial class ConfirmSheet(ShellViewModel shell) : ObservableObject, ISheet
{
    public string Title { get; init; } = "";
    public string Message { get; init; } = "";
    public string? Detail { get; init; }
    public string ConfirmText { get; init; } = "OK";
    public string CancelText { get; init; } = "Cancel";
    public bool IsDestructive { get; init; }
    public Action OnConfirm { get; init; } = () => { };

    [RelayCommand]
    private void Confirm()
    {
        shell.CloseSheet();
        OnConfirm();
    }

    [RelayCommand]
    public void Cancel() => shell.CloseSheet();
}

public sealed partial class SecretEditorSheet(ShellViewModel shell) : ObservableObject, ISheet
{
    public string Title { get; init; } = "";
    public string ConfirmText { get; init; } = "Save";
    public Action<SecretKind, string, string> OnSave { get; init; } = (_, _, _) => { };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsKey), nameof(NamePlaceholder))]
    public partial bool IsNote { get; set; }

    public bool IsKey
    {
        get => !IsNote;
        set => IsNote = !value;
    }

    public string NamePlaceholder => IsNote ? "Title, like “Recovery codes”" : "Name, like STRIPE_SECRET_KEY";

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    [ObservableProperty]
    public partial string Value { get; set; } = "";

    [ObservableProperty]
    public partial string Error { get; set; } = "";

    [RelayCommand]
    private void Save()
    {
        var name = Name.Trim();
        if (name.Length == 0)
        {
            Error = IsNote ? "Give the note a title." : "Give the secret a name.";
            return;
        }

        var value = CleanValue();
        shell.CloseSheet();
        OnSave(IsNote ? SecretKind.Note : SecretKind.KeyValue, name, value);
    }

    [RelayCommand]
    private void CopyName() => shell.CopyToClipboard(Name.Trim(), IsNote ? "the title" : "the name");

    [RelayCommand]
    private void CopyValue() => shell.CopyToClipboard(CleanValue(), IsNote ? "the note" : "the value");

    // Keys are single values: trim stray spaces and newlines from pasting. Notes keep their text as-is.
    private string CleanValue() => IsNote ? Value.Replace("\r\n", "\n") : Value.Trim();

    [RelayCommand]
    public void Cancel() => shell.CloseSheet();
}

public sealed partial class ChangePassphraseSheet(ShellViewModel shell, Action onChanged) : ObservableObject, ISheet
{
    [ObservableProperty]
    public partial string Current { get; set; } = "";

    [ObservableProperty]
    public partial string New { get; set; } = "";

    [ObservableProperty]
    public partial string Confirm { get; set; } = "";

    [ObservableProperty]
    public partial string Error { get; set; } = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial bool IsBusy { get; set; }

    private bool CanSave() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        Error = "";
        if (New.Length < SetupViewModel.MinimumLength)
        {
            Error = $"Use at least {SetupViewModel.MinimumLength} characters.";
            return;
        }

        if (New != Confirm)
        {
            Error = "The new passphrases don't match.";
            return;
        }

        IsBusy = true;
        try
        {
            var (current, next) = (Current, New);
            var vault = shell.Session;
            await Task.Run(() => vault.ChangePassphrase(current, next));
            Current = New = Confirm = "";
            shell.Settings.LastPassphraseUnlock = DateTimeOffset.UtcNow;
            shell.SaveSettings();
            shell.BackUp(announce: false);
            shell.CloseSheet();
            shell.ShowToast("Passphrase changed");
            onChanged();
        }
        catch (WrongPassphraseException)
        {
            Current = "";
            Error = "Your current passphrase isn't right.";
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
    public void Cancel()
    {
        if (!IsBusy)
            shell.CloseSheet();
    }
}
