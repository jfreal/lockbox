using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Lockbox.App.Services;
using Lockbox.App.ViewModels;
using Lockbox.Core;
using Lockbox.Core.Crypto;
using Lockbox.Core.Vault;

namespace Lockbox.App;

/// <summary>
/// Debug-only: drives the real view models end to end against a throwaway vault in %TEMP%.
/// Run: Lockbox.exe --selftest &lt;report.txt&gt;. Exit code = number of failed checks.
/// </summary>
internal static class SelfTest
{
    private const string Passphrase = "orbit maple tunnel crisp lantern";
    private const string NewPassphrase = "harbor velvet quartz meadow ember";

    private static readonly List<string> Lines = [];
    private static int _failures;

    public static async Task<int> RunAsync(string reportPath)
    {
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var dataDir = Directory.CreateTempSubdirectory("lockbox-selftest-").FullName;
        string? savedClipboard = null;
        try
        {
            if (Clipboard.ContainsText())
                savedClipboard = Clipboard.GetText();
            await RunStepsAsync(dataDir);
        }
        catch (Exception ex)
        {
            Fail($"crashed: {ex}");
        }
        finally
        {
            if (savedClipboard is not null)
                Clipboard.SetText(savedClipboard);
            Directory.Delete(dataDir, recursive: true);
            Lines.Add(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
            await File.WriteAllLinesAsync(reportPath, Lines);
        }

        return _failures;
    }

    private static async Task RunStepsAsync(string dataDir)
    {
        ThemeService.Apply();
        var dropbox = Path.Combine(dataDir, "Dropbox");
        Directory.CreateDirectory(dropbox);
        var settings = new AppSettings { BackupFolder = Path.Combine(dropbox, "Lockbox") };
        var vaultPath = Path.Combine(dataDir, "vault.lockbox");
        var shell = new ShellViewModel(new VaultStore(vaultPath, new KdfParameters(8 * 1024, 1, 1)), settings,
            Path.Combine(dataDir, "settings.json"), new WindowsHelloService(), new SecureClipboard());
        var window = new MainWindow(shell, protectFromCapture: false)
        {
            WindowStartupLocation = WindowStartupLocation.Manual, Left = -30000, Top = -30000, ShowActivated = false,
        };
        window.Show();

        // --- First run ---
        await shell.StartAsync(promptForHello: false);
        Check("first run shows setup", shell.CurrentScreen is SetupViewModel);
        Lines.Add($"info: Windows Hello available on this PC = {shell.HelloAvailable}");
        var setup = (SetupViewModel)shell.CurrentScreen!;

        // Type into the real PasswordBoxes, the way a person does.
        var boxes = await PasswordBoxesAsync(window);
        Check("setup shows two passphrase boxes", boxes.Count == 2);
        Check("create disabled before typing", !setup.CreateCommand.CanExecute(null));
        boxes[0].Password = Passphrase;
        boxes[1].Password = Passphrase;
        Check("typing reaches the view model", setup.Passphrase == Passphrase && setup.Confirm == Passphrase);
        Check("create enabled after typing", setup.CreateCommand.CanExecute(null));
        setup.Passphrase = setup.Confirm = "";
        Check("clearing the view model clears the boxes", boxes[0].Password.Length == 0 && boxes[1].Password.Length == 0);

        setup.Passphrase = "short";
        setup.Confirm = "short";
        await setup.CreateCommand.ExecuteAsync(null);
        Check("too-short passphrase rejected", setup.Error.Contains("at least") && !shell.Store.Exists);

        setup.Passphrase = Passphrase;
        setup.Confirm = Passphrase + "x";
        await setup.CreateCommand.ExecuteAsync(null);
        Check("mismatched passphrase rejected", setup.Error.Contains("match") && !shell.Store.Exists);

        setup.Confirm = Passphrase;
        await setup.CreateCommand.ExecuteAsync(null);
        Check("vault created and open", shell.Store.Exists && shell.IsUnlocked);
        Check("passphrase fields cleared after create", setup.Passphrase.Length == 0 && setup.Confirm.Length == 0);
        if (setup.IsHelloStep)
            setup.SkipHelloCommand.Execute(null);
        Check("vault screen shown", shell.CurrentScreen is VaultViewModel);
        Check("first backup written", File.Exists(Path.Combine(settings.BackupFolder, BackupService.LatestFileName)));
        var vault = shell.Vault!;
        Check("empty state shown", vault.ShowEmptyState);

        // --- Projects ---
        vault.NewProjectCommand.Execute(null);
        var prompt = (TextPromptSheet)shell.Sheet!;
        prompt.Text = "  alpha  ";
        prompt.ConfirmCommand.Execute(null);
        Check("project created, trimmed, selected", vault.SelectedProject?.Name == "alpha" && shell.Sheet is null);

        vault.NewProjectCommand.Execute(null);
        prompt = (TextPromptSheet)shell.Sheet!;
        prompt.Text = "ALPHA";
        prompt.ConfirmCommand.Execute(null);
        Check("duplicate project name rejected", prompt.Error.Contains("already") && shell.Sheet == prompt);
        prompt.Cancel();

        // --- Secrets ---
        vault.AddSecretCommand.Execute(null);
        var editor = (SecretEditorSheet)shell.Sheet!;
        editor.Name = "API_KEY";
        editor.Value = "  abc123 \r\n";
        editor.SaveCommand.Execute(null);
        var key = vault.SelectedProject!.Secrets.Single(s => s.Name == "API_KEY");
        Check("key value trimmed", key.Value == "abc123");

        vault.AddSecretCommand.Execute(null);
        editor = (SecretEditorSheet)shell.Sheet!;
        editor.IsNote = true;
        editor.Name = "Notes";
        editor.Value = "line one\r\nline two";
        editor.SaveCommand.Execute(null);
        var note = vault.SelectedProject.Secrets.Single(s => s.Name == "Notes");
        Check("note keeps lines", note.IsNote && note.Value == "line one\nline two" && note.DisplayValue == "Note · 2 lines");
        Check("values masked by default", key.DisplayValue.StartsWith('•'));
        vault.ToggleRevealCommand.Execute(key);
        Check("reveal shows value", key.DisplayValue == "abc123");

        // --- Clipboard ---
        vault.CopyCommand.Execute(key);
        // Other apps (clipboard managers, Windows' own history) can hold the clipboard for a moment.
        var copied = "";
        for (var attempt = 0; attempt < 10 && copied != "abc123"; attempt++)
        {
            try
            {
                copied = Clipboard.GetText();
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
            }

            if (copied != "abc123")
                await Task.Delay(50);
        }

        var data = Clipboard.GetDataObject();
        Check("copy puts value on clipboard", copied == "abc123");
        Check("copy opts out of clipboard history",
            data is not null && data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") && data.GetDataPresent("CanIncludeInClipboardHistory"));
        shell.Clipboard.ClearIfOurs();
        Check("clipboard cleared", !Clipboard.ContainsText());

        // --- Search + edit through a search row ---
        vault.SearchText = "api";
        Check("search finds by name", vault.SearchResults.Count == 1 && vault.SearchResults[0].ShowProject);
        vault.EditSecretCommand.Execute(vault.SearchResults[0]);
        editor = (SecretEditorSheet)shell.Sheet!;
        editor.Name = "API_KEY_PROD";
        editor.SaveCommand.Execute(null);
        Check("edit from search updates the project", vault.SelectedProject.Secrets.Any(s => s.Name == "API_KEY_PROD"));
        vault.SearchText = "";

        // --- Lock / unlock ---
        vault.LockCommand.Execute(null);
        Check("lock shows unlock screen", shell.CurrentScreen is UnlockViewModel && !shell.IsUnlocked);
        var unlock = (UnlockViewModel)shell.CurrentScreen!;
        Check("no Hello enrolled -> passphrase mode", unlock.UsePassphrase);
        var unlockBoxes = await PasswordBoxesAsync(window);
        unlockBoxes.Single().Password = "typed";
        Check("typing on unlock screen reaches the view model", unlock.Passphrase == "typed" && unlock.UnlockWithPassphraseCommand.CanExecute(null));
        unlock.Passphrase = "wrong passphrase entirely";
        await unlock.UnlockWithPassphraseCommand.ExecuteAsync(null);
        Check("wrong passphrase refused", !shell.IsUnlocked && unlock.Error.Length > 0 && unlock.Passphrase.Length == 0);
        unlock.Passphrase = Passphrase;
        await unlock.UnlockWithPassphraseCommand.ExecuteAsync(null);
        vault = shell.Vault!;
        Check("unlock restores data", shell.IsUnlocked && vault.Projects.Single().Secrets.Count == 2);

        // --- Notes ---
        var alpha = vault.SelectedProject!;
        vault.NewNoteCommand.Execute(null);
        Check("new note opens the editor", vault.ShowNote && !vault.ShowProject && vault.EditingNote!.IsNew);
        vault.EditingNote!.CloseCommand.Execute(null);
        Check("closing an empty note adds nothing", !vault.ShowNote && alpha.Secrets.Count == 2);

        vault.NewNoteCommand.Execute(null);
        var noteEditor = vault.EditingNote!;
        noteEditor.Title = "Runbook";
        noteEditor.Body = "step one\r\nstep two";
        await Task.Delay(NoteEditorViewModel.SaveDelay + TimeSpan.FromMilliseconds(600));
        var runbook = alpha.Secrets.SingleOrDefault(s => s.Name == "Runbook");
        Check("note saves itself after a pause", runbook is { IsNote: true } && runbook.Value == "step one\nstep two" && noteEditor.Status == "Saved");
        Check("note text is not on disk in plain text", !File.ReadAllText(vaultPath).Contains("step one"));
        noteEditor.CloseCommand.Execute(null);

        vault.NewNoteCommand.Execute(null);
        vault.EditingNote!.Body = "\n   first real line   \nmore";
        vault.EditingNote!.CloseCommand.Execute(null);
        Check("untitled note is named from its first line", alpha.Secrets.Any(s => s.IsNote && s.Name == "first real line"));

        vault.EditSecretCommand.Execute(runbook);
        Check("opening a note shows its text", vault.EditingNote is { IsNew: false } open && open.Body == "step one\nstep two");
        vault.SearchText = "run";
        Check("searching closes the editor", !vault.ShowNote && vault.ShowSearch);
        vault.SearchText = "";

        vault.EditSecretCommand.Execute(runbook);
        vault.EditingNote!.Body = "edited just before locking";
        vault.LockCommand.Execute(null);
        unlock = (UnlockViewModel)shell.CurrentScreen!;
        unlock.Passphrase = Passphrase;
        await unlock.UnlockWithPassphraseCommand.ExecuteAsync(null);
        vault = shell.Vault!;
        alpha = vault.Projects.Single();
        Check("locking saves the open note first", alpha.Secrets.Single(s => s.Name == "Runbook").Value == "edited just before locking");

        vault.EditSecretCommand.Execute(alpha.Secrets.Single(s => s.Name == "Runbook"));
        vault.EditingNote!.DeleteCommand.Execute(null);
        ((ConfirmSheet)shell.Sheet!).ConfirmCommand.Execute(null);
        Check("deleting a note removes it and closes the editor", !vault.ShowNote && alpha.Secrets.All(s => s.Name != "Runbook"));
        alpha.Remove(alpha.Secrets.Single(s => s.Name == "first real line"));
        shell.Save();

        // --- Import ---
        vault.OpenImportCommand.Execute(null);
        var import = (ImportViewModel)shell.CurrentScreen!;
        import.RawText = """
            alpha
            NEW_TOKEN=tok_1234567890
            ## beta
            export DB_URL="postgres://u:p@h/db"
            just a sentence about something, not a secret.
            """;
        import.FindSecretsCommand.Execute(null);
        Check("import finds 2 secrets in 2 groups", import.Groups.Count == 2 && import.SelectedCount == 2 && import.LeftoverCount == 1);
        Check("import marks existing project", import.Groups[0].Destination.Contains("existing"));
        import.SaveLeftovers = true;
        import.ImportCommand.Execute(null);
        vault = shell.Vault!;
        Check("import adds to alpha and creates beta",
            vault.Projects.Count == 2
            && vault.Projects.First(p => p.Name == "alpha").Secrets.Any(s => s.Name == "NEW_TOKEN")
            && vault.Projects.First(p => p.Name == "beta").Secrets.Any(s => s.Value == "postgres://u:p@h/db"));
        Check("leftovers saved as a note", vault.Projects.First(p => p.Name == "alpha").Secrets.Any(s => s.IsNote && s.Value.Contains("just a sentence")));
        Check("cleanup reminder shown", shell.Sheet is ConfirmSheet);
        shell.Sheet?.Cancel();

        // --- Change passphrase ---
        var settingsVm = new SettingsViewModel(shell);
        settingsVm.ChangePassphraseCommand.Execute(null);
        var change = (ChangePassphraseSheet)shell.Sheet!;
        change.Current = "not it";
        change.New = change.Confirm = NewPassphrase;
        await change.SaveCommand.ExecuteAsync(null);
        Check("change refused with wrong current", change.Error.Contains("isn't right"));
        change.Current = Passphrase;
        change.New = change.Confirm = NewPassphrase;
        await change.SaveCommand.ExecuteAsync(null);
        Check("passphrase changed", shell.Sheet is null);

        // --- Backups ---
        var latest = Path.Combine(settings.BackupFolder, BackupService.LatestFileName);
        using (var fromBackup = new VaultStore(latest, new KdfParameters(8 * 1024, 1, 1)).UnlockWithPassphrase(NewPassphrase))
            Check("Dropbox copy opens with new passphrase and has all data", fromBackup.Data.Projects.Count == 2);
        Check("daily snapshot exists", Directory.GetFiles(Path.Combine(settings.BackupFolder, BackupService.HistoryFolderName)).Length == 1);
        Check("vault file has no plaintext", !File.ReadAllText(vaultPath).Contains("abc123") && !File.ReadAllText(vaultPath).Contains("alpha"));

        // --- Delete ---
        vault = shell.Vault!;
        vault.SelectedProject = vault.Projects.First(p => p.Name == "beta");
        vault.DeleteSecretCommand.Execute(vault.SelectedProject.Secrets[0]);
        ((ConfirmSheet)shell.Sheet!).ConfirmCommand.Execute(null);
        Check("secret deleted", vault.SelectedProject.Secrets.Count == 0);
        vault.DeleteProjectCommand.Execute(null);
        ((ConfirmSheet)shell.Sheet!).ConfirmCommand.Execute(null);
        Check("project deleted, neighbour selected", vault.Projects.Count == 1 && vault.SelectedProject?.Name == "alpha");

        // --- Idle lock ---
        shell.LockIfIdle();
        Check("not locked while active", shell.IsUnlocked);
        typeof(ShellViewModel).GetField("_lastActivityUtc", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(shell, DateTime.UtcNow - AppSettings.AutoLockAfter - TimeSpan.FromSeconds(1));
        shell.LockIfIdle();
        Check("locks after 5 idle minutes", !shell.IsUnlocked && shell.CurrentScreen is UnlockViewModel);

        // --- Restart with the new passphrase ---
        var shell2 = new ShellViewModel(new VaultStore(vaultPath, new KdfParameters(8 * 1024, 1, 1)), settings,
            Path.Combine(dataDir, "settings.json"), new WindowsHelloService(), new SecureClipboard());
        await shell2.StartAsync(promptForHello: false);
        var unlock2 = (UnlockViewModel)shell2.CurrentScreen!;
        unlock2.Passphrase = NewPassphrase;
        await unlock2.UnlockWithPassphraseCommand.ExecuteAsync(null);
        Check("fresh start unlocks with new passphrase", shell2.IsUnlocked && shell2.Vault!.Projects.Single().Name == "alpha");
        shell2.Lock();

        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        window.Close();
    }

    private static async Task<List<System.Windows.Controls.PasswordBox>> PasswordBoxesAsync(Window window)
    {
        await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var found = new List<System.Windows.Controls.PasswordBox>();
        void Walk(DependencyObject node)
        {
            if (node is System.Windows.Controls.PasswordBox { IsVisible: true } box)
                found.Add(box);
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(node); i++)
                Walk(System.Windows.Media.VisualTreeHelper.GetChild(node, i));
        }

        Walk(window);
        return found;
    }

    private static void Check(string name, bool ok)
    {
        Lines.Add($"{(ok ? "PASS" : "FAIL")}  {name}");
        if (!ok)
            _failures++;
    }

    private static void Fail(string message)
    {
        Lines.Add($"FAIL  {message}");
        _failures++;
    }
}
