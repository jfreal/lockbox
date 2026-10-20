using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lockbox.Core.Vault;

namespace Lockbox.App.ViewModels;

public sealed partial class VaultViewModel : ObservableObject
{
    private readonly ShellViewModel _shell;
    private readonly VaultData _data;

    public VaultViewModel(ShellViewModel shell, VaultData data)
    {
        _shell = shell;
        _data = data;
        foreach (var project in data.Projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            Projects.Add(new ProjectViewModel(project));
        Projects.CollectionChanged += (_, _) => RefreshLayout();
        SelectedProject = Projects.FirstOrDefault();
    }

    public ObservableCollection<ProjectViewModel> Projects { get; } = [];

    public ObservableCollection<SecretViewModel> SearchResults { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProject))]
    public partial ProjectViewModel? SelectedProject { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSearching), nameof(ShowSearch), nameof(ShowProject), nameof(ShowEmptyState))]
    public partial string SearchText { get; set; } = "";

    /// <summary>The note open in the full-pane editor, if any. It covers the project and search views.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowNote), nameof(ShowSearch), nameof(ShowProject), nameof(ShowEmptyState))]
    public partial NoteEditorViewModel? EditingNote { get; set; }

    public bool IsSearching => !string.IsNullOrWhiteSpace(SearchText);

    public bool ShowNote => EditingNote is not null;

    public bool ShowSearch => IsSearching && !ShowNote;

    public bool ShowProject => !IsSearching && !ShowNote && SelectedProject is not null;

    public bool ShowEmptyState => !IsSearching && !ShowNote && Projects.Count == 0;

    public string SearchSummary => SearchResults.Count switch
    {
        0 => $"Nothing matches “{SearchText.Trim()}”.",
        1 => "1 result",
        var n => $"{n} results",
    };

    public bool HasSearchResults => SearchResults.Count > 0;

    partial void OnSelectedProjectChanged(ProjectViewModel? oldValue, ProjectViewModel? newValue)
    {
        oldValue?.HideAll();
        if (EditingNote is { } editor && editor.Project != newValue)
            CloseNote();
    }

    partial void OnSearchTextChanged(string value)
    {
        if (IsSearching)
            CloseNote();
        RunSearch();
    }

    private void RunSearch()
    {
        foreach (var result in SearchResults)
            result.IsRevealed = false;
        SearchResults.Clear();

        var query = SearchText.Trim();
        if (query.Length > 0)
        {
            var matches = Projects
                .SelectMany(p => p.Secrets)
                .Where(s => s.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                            || s.ProjectName.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .OrderBy(s => s.ProjectName, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase);
            foreach (var match in matches)
                SearchResults.Add(match.ForSearch());
        }

        OnPropertyChanged(nameof(SearchSummary));
        OnPropertyChanged(nameof(HasSearchResults));
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    [RelayCommand]
    private void NewProject() =>
        _shell.OpenSheet(new TextPromptSheet(_shell)
        {
            Title = "New project",
            Message = "One project per app, site, or client works well.",
            Placeholder = "Project name",
            ConfirmText = "Create",
            Validate = name => ValidateProjectName(name, null),
            OnConfirm = name =>
            {
                var project = new Project { Name = name };
                _data.Projects.Add(project);
                var vm = new ProjectViewModel(project);
                InsertSorted(vm);
                SearchText = "";
                SelectedProject = vm;
                _shell.Save();
            },
        });

    [RelayCommand]
    private void RenameProject()
    {
        if (SelectedProject is not { } project)
            return;

        _shell.OpenSheet(new TextPromptSheet(_shell)
        {
            Title = "Rename project",
            Placeholder = "Project name",
            Text = project.Name,
            ConfirmText = "Rename",
            Validate = name => ValidateProjectName(name, project),
            OnConfirm = name =>
            {
                project.Name = name;
                Projects.Remove(project);
                InsertSorted(project);
                SelectedProject = project;
                _shell.Save();
            },
        });
    }

    [RelayCommand]
    private void DeleteProject()
    {
        if (SelectedProject is not { } project)
            return;

        _shell.OpenSheet(new ConfirmSheet(_shell)
        {
            Title = $"Delete “{project.Name}”?",
            Message = project.Count == 0
                ? "This project is empty."
                : $"This deletes the project and its {project.CountText}. Your daily backups in the backup folder still have them for 10 days.",
            ConfirmText = "Delete project",
            IsDestructive = true,
            OnConfirm = () =>
            {
                CloseNoteIfIn(project);
                var index = Projects.IndexOf(project);
                _data.Projects.Remove(project.Model);
                Projects.Remove(project);
                SelectedProject = Projects.Count == 0 ? null : Projects[Math.Min(index, Projects.Count - 1)];
                _shell.Save();
            },
        });
    }

    [RelayCommand]
    private void AddSecret()
    {
        if (SelectedProject is not { } project)
            return;

        _shell.OpenSheet(new SecretEditorSheet(_shell)
        {
            Title = $"New key in {project.Name}",
            ConfirmText = "Add",
            OnSave = (kind, name, value) =>
            {
                project.Add(new Secret { Kind = kind, Name = name, Value = value });
                _shell.Save();
            },
        });
    }

    [RelayCommand]
    private void EditSecret(SecretViewModel? secret)
    {
        if (secret is null)
            return;

        var original = Original(secret);
        if (original.IsNote)
        {
            OpenNote(original);
            return;
        }

        _shell.OpenSheet(new SecretEditorSheet(_shell)
        {
            Title = "Edit secret",
            ConfirmText = "Save",
            IsNote = secret.IsNote,
            Name = secret.Name,
            Value = secret.Value,
            OnSave = (kind, name, value) =>
            {
                original.Update(kind, name, value);
                original.Project.Resort(original);
                if (!ReferenceEquals(original, secret))
                    secret.Update(kind, name, value);
                _shell.Save();
            },
        });
    }

    [RelayCommand]
    private void DeleteSecret(SecretViewModel? secret)
    {
        if (secret is null)
            return;

        _shell.OpenSheet(new ConfirmSheet(_shell)
        {
            Title = $"Delete “{secret.Name}”?",
            Message = $"It will be removed from {secret.ProjectName}.",
            ConfirmText = "Delete",
            IsDestructive = true,
            OnConfirm = () =>
            {
                var original = Original(secret);
                original.Project.Remove(original);
                SearchResults.Remove(secret);
                if (IsSearching)
                {
                    OnPropertyChanged(nameof(SearchSummary));
                    OnPropertyChanged(nameof(HasSearchResults));
                }

                _shell.Save();
            },
        });
    }

    [RelayCommand]
    private void Copy(SecretViewModel? secret)
    {
        if (secret is not null)
            _shell.CopyToClipboard(secret.Value, secret.Name);
    }

    [RelayCommand]
    private void CopyName(SecretViewModel? secret)
    {
        if (secret is not null)
            _shell.CopyToClipboard(secret.Name, $"the name {secret.Name}");
    }

    [RelayCommand]
    private static void ToggleReveal(SecretViewModel? secret)
    {
        if (secret is not null)
            secret.IsRevealed = !secret.IsRevealed;
    }

    [RelayCommand]
    private void NewNote()
    {
        if (SelectedProject is { } project)
            OpenEditor(new NoteEditorViewModel(_shell, this, project, null));
    }

    public void OpenNote(SecretViewModel note)
    {
        // Show the note's own project behind the editor, so closing it lands somewhere sensible.
        if (IsSearching)
            SearchText = "";
        SelectedProject = note.Project;
        OpenEditor(new NoteEditorViewModel(_shell, this, note.Project, note));
    }

    private void OpenEditor(NoteEditorViewModel editor)
    {
        CloseNote();
        EditingNote = editor;
    }

    /// <summary>Saves and closes the open note. Called before locking, navigating, or opening another note.</summary>
    public void CloseNote(NoteEditorViewModel? only = null)
    {
        if (EditingNote is not { } editor || (only is not null && only != editor))
            return;
        EditingNote = null;
        editor.Commit();
    }

    [RelayCommand]
    private void OpenImport()
    {
        CloseNote();
        _shell.CurrentScreen = new ImportViewModel(_shell, this, _data);
    }

    [RelayCommand]
    private void OpenSettings()
    {
        CloseNote();
        _shell.CurrentScreen = new SettingsViewModel(_shell);
    }

    [RelayCommand]
    private void Lock() => _shell.Lock();

    /// <summary>Called by import after it changes the data underneath.</summary>
    public void Reload(Guid? selectProjectId)
    {
        Projects.Clear();
        foreach (var project in _data.Projects.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase))
            Projects.Add(new ProjectViewModel(project));
        SearchText = "";
        SelectedProject = Projects.FirstOrDefault(p => p.Model.Id == selectProjectId) ?? Projects.FirstOrDefault();
    }

    public string? ValidateProjectName(string name, ProjectViewModel? except)
    {
        if (name.Length == 0)
            return "Give the project a name.";
        if (Projects.Any(p => p != except && string.Equals(p.Name, name, StringComparison.CurrentCultureIgnoreCase)))
            return "You already have a project with that name.";
        return null;
    }

    // Search rows wrap the same Secret model in their own view model; edits go to the project's copy.
    private static SecretViewModel Original(SecretViewModel secret) =>
        secret.Project.Secrets.FirstOrDefault(s => s.Model == secret.Model) ?? secret;

    private void InsertSorted(ProjectViewModel project)
    {
        var index = 0;
        while (index < Projects.Count && string.Compare(Projects[index].Name, project.Name, StringComparison.CurrentCultureIgnoreCase) <= 0)
            index++;
        Projects.Insert(index, project);
    }

    private void RefreshLayout()
    {
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(ShowProject));
    }

    // Project deletion while its note is open: close the editor first.
    private void CloseNoteIfIn(ProjectViewModel project)
    {
        if (EditingNote?.Project == project)
            CloseNote();
    }
}
