using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lockbox.Core.Import;
using Lockbox.Core.Vault;
using Microsoft.Win32;

namespace Lockbox.App.ViewModels;

/// <summary>Paste or open messy notes, review what the parser found, then add it to projects.</summary>
public sealed partial class ImportViewModel(ShellViewModel shell, VaultViewModel vault, VaultData data) : ObservableObject
{
    public static string NotepadTabStateFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        @"Packages\Microsoft.WindowsNotepad_8wekyb3d8bbwe\LocalState\TabState");

    public ObservableCollection<ImportGroupViewModel> Groups { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(FindSecretsCommand))]
    public partial string RawText { get; set; } = "";

    [ObservableProperty]
    public partial bool IsReviewing { get; set; }

    [ObservableProperty]
    public partial string Error { get; set; } = "";

    [ObservableProperty]
    public partial string Leftovers { get; set; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftoverTitle))]
    public partial int LeftoverCount { get; set; }

    public string LeftoverTitle => LeftoverCount == 1 ? "1 other line" : $"{LeftoverCount} other lines";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ImportCommand))]
    public partial bool SaveLeftovers { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LeftoverGlyph))]
    public partial bool RevealLeftovers { get; set; }

    public string LeftoverGlyph => RevealLeftovers ? "" : "";

    [RelayCommand]
    private void ToggleLeftovers() => RevealLeftovers = !RevealLeftovers;

    [ObservableProperty]
    public partial string LeftoverProjectName { get; set; } = "";

    public bool HasLeftovers => LeftoverCount > 0;

    public int SelectedCount => Groups.Sum(g => g.Items.Count(i => i.IsSelected));

    public string Summary
    {
        get
        {
            var found = Groups.Sum(g => g.Items.Count);
            var groups = Groups.Count == 1 ? "1 group" : $"{Groups.Count} groups";
            return found == 1 ? $"Found 1 secret in {groups}." : $"Found {found} secrets in {groups}.";
        }
    }

    public string ImportButtonText
    {
        get
        {
            var n = SelectedCount + (SaveLeftovers ? 1 : 0);
            return n == 1 ? "Import 1 item" : $"Import {n} items";
        }
    }

    private bool CanFind() => RawText.Trim().Length > 0;

    [RelayCommand(CanExecute = nameof(CanFind))]
    private void FindSecrets()
    {
        Error = "";
        var result = SecretTextParser.Parse(RawText);
        if (result.Groups.Count == 0 && result.Leftovers.Count == 0)
        {
            Error = "Nothing to import.";
            return;
        }

        foreach (var old in Groups)
            old.Unhook();
        Groups.Clear();

        var fallback = vault.SelectedProject?.Name ?? "Imported";
        foreach (var group in result.Groups)
            Groups.Add(new ImportGroupViewModel(group.Heading ?? fallback, group.Items, Existing, OnSelectionChanged));

        Leftovers = string.Join(Environment.NewLine, result.Leftovers);
        LeftoverCount = result.Leftovers.Count;
        LeftoverProjectName = Groups.FirstOrDefault()?.ProjectName ?? fallback;
        SaveLeftovers = Groups.Count == 0;
        RevealLeftovers = false;
        IsReviewing = true;
        OnPropertyChanged(nameof(HasLeftovers));
        OnPropertyChanged(nameof(Summary));
        OnSelectionChanged();
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a text file",
            Filter = "Text files (*.txt;*.env;*.md;*.json)|*.txt;*.env;*.md;*.json;.env*|All files (*.*)|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (dialog.ShowDialog(Application.Current.MainWindow) != true)
            return;

        try
        {
            var text = File.ReadAllText(dialog.FileName);
            RawText = RawText.Trim().Length == 0 ? text : RawText.TrimEnd() + Environment.NewLine + Environment.NewLine + text;
            FindSecrets();
        }
        catch (IOException ex)
        {
            Error = ErrorText.Of(ex);
        }
    }

    [RelayCommand]
    private void StartOver()
    {
        IsReviewing = false;
        Error = "";
    }

    private bool CanImport() => SelectedCount > 0 || SaveLeftovers;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private void Import()
    {
        Error = "";
        var touched = new List<Project>();
        var added = 0;

        foreach (var group in Groups)
        {
            var selected = group.Items.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0)
                continue;

            var project = FindOrCreate(group.ProjectName);
            touched.Add(project);
            foreach (var item in selected)
            {
                var name = item.Name.Trim().Length == 0 ? "Unnamed secret" : item.Name.Trim();
                if (project.Secrets.Any(s => s.Name == name && s.Value == item.Value))
                    continue; // Exact duplicate.
                project.Secrets.Add(new Secret { Kind = SecretKind.KeyValue, Name = name, Value = item.Value });
                added++;
            }
        }

        if (SaveLeftovers && LeftoverCount > 0)
        {
            var project = FindOrCreate(LeftoverProjectName);
            touched.Add(project);
            project.Secrets.Add(new Secret
            {
                Kind = SecretKind.Note,
                Name = $"Imported notes {DateTime.Now:yyyy-MM-dd}",
                Value = Leftovers.Replace("\r\n", "\n"),
            });
            added++;
        }

        if (!shell.Save())
            return;

        RawText = Leftovers = "";
        foreach (var group in Groups)
            group.Unhook();
        Groups.Clear();

        vault.Reload(touched.FirstOrDefault()?.Id);
        shell.ShowVault();
        shell.ShowToast(added == 1 ? "Imported 1 item" : $"Imported {added} items");
        shell.OpenSheet(new ConfirmSheet(shell)
        {
            Title = "Now clean up the old copies",
            Message = "Delete the Notepad files you imported from. Windows 11 Notepad also keeps hidden copies of open tabs, even ones you never saved.",
            Detail = "Close Notepad first, then delete everything in its auto-save folder.",
            ConfirmText = "Open that folder",
            CancelText = "Later",
            OnConfirm = () =>
            {
                if (Directory.Exists(NotepadTabStateFolder))
                    Process.Start(new ProcessStartInfo("explorer.exe", $"\"{NotepadTabStateFolder}\"") { UseShellExecute = true });
                else
                    shell.ShowToast("Notepad's auto-save folder isn't on this PC. Nothing to clean.");
            },
        });
    }

    [RelayCommand]
    private void Back()
    {
        RawText = Leftovers = "";
        Groups.Clear();
        shell.ShowVault();
    }

    private Project FindOrCreate(string rawName)
    {
        var name = rawName.Trim().Length == 0 ? "Imported" : rawName.Trim();
        var project = data.Projects.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase));
        if (project is null)
        {
            project = new Project { Name = name };
            data.Projects.Add(project);
        }

        return project;
    }

    private bool Existing(string name) =>
        data.Projects.Any(p => string.Equals(p.Name, name.Trim(), StringComparison.CurrentCultureIgnoreCase));

    private void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ImportButtonText));
        ImportCommand.NotifyCanExecuteChanged();
    }
}

public sealed partial class ImportGroupViewModel : ObservableObject
{
    private readonly Func<string, bool> _exists;
    private readonly Action _selectionChanged;

    public ImportGroupViewModel(string projectName, IEnumerable<ImportCandidate> items, Func<string, bool> exists, Action selectionChanged)
    {
        _exists = exists;
        _selectionChanged = selectionChanged;
        ProjectName = projectName;
        foreach (var item in items)
        {
            var vm = new ImportItemViewModel { Name = item.Name, Value = item.Value, IsSelected = item.Suggested };
            vm.PropertyChanged += OnItemChanged;
            Items.Add(vm);
        }
    }

    public ObservableCollection<ImportItemViewModel> Items { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Destination))]
    public partial string ProjectName { get; set; }

    public string Destination => ProjectName.Trim().Length == 0
        ? "Goes to a project called “Imported”"
        : _exists(ProjectName) ? "Adds to your existing project" : "Creates a new project";

    public void Unhook()
    {
        foreach (var item in Items)
            item.PropertyChanged -= OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportItemViewModel.IsSelected))
            _selectionChanged();
    }
}

public sealed partial class ImportItemViewModel : ObservableObject
{
    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; } = "";

    public string Value { get; init; } = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayValue), nameof(RevealGlyph))]
    public partial bool IsRevealed { get; set; }

    public string DisplayValue => IsRevealed ? Value : "••••••••••••";

    public string RevealGlyph => IsRevealed ? "" : "";

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;
}
