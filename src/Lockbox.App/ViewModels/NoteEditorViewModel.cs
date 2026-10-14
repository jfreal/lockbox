using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Lockbox.Core.Vault;

namespace Lockbox.App.ViewModels;

/// <summary>
/// Full-pane editor for a note. Saves on its own a moment after typing stops, and again on close.
/// A new note isn't added to the project until it has a title or some text.
/// </summary>
public sealed partial class NoteEditorViewModel : ObservableObject
{
    public static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly ShellViewModel _shell;
    private readonly VaultViewModel _vault;
    private readonly DispatcherTimer _saveTimer = new() { Interval = SaveDelay };
    private SecretViewModel? _note;
    private bool _dirty;
    private bool _loading = true;

    public NoteEditorViewModel(ShellViewModel shell, VaultViewModel vault, ProjectViewModel project, SecretViewModel? note)
    {
        _shell = shell;
        _vault = vault;
        _note = note;
        Project = project;
        IsNew = note is null;
        Title = note?.Name ?? "";
        Body = note?.Value ?? "";
        _loading = false;
        _saveTimer.Tick += (_, _) => Commit();
    }

    public ProjectViewModel Project { get; }

    /// <summary>True until the note is first saved. The view focuses the title for new notes, the text otherwise.</summary>
    public bool IsNew { get; }

    [ObservableProperty]
    public partial string Title { get; set; }

    [ObservableProperty]
    public partial string Body { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    public string BackText => $"‹ {Project.Name}";

    partial void OnTitleChanged(string value) => MarkDirty();

    partial void OnBodyChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (_loading)
            return;
        _dirty = true;
        Status = "Editing";
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes pending changes to the vault. Safe to call any time; does nothing when clean.</summary>
    public void Commit()
    {
        _saveTimer.Stop();
        if (!_dirty)
            return;
        _dirty = false;

        var body = Body.Replace("\r\n", "\n");
        var name = TitleOrFirstLine(Title, body);
        if (_note is null)
        {
            if (name.Length == 0)
            {
                Status = "";
                return; // Nothing typed yet; don't create an empty note.
            }

            _note = Project.Add(new Secret { Kind = SecretKind.Note, Name = name, Value = body });
        }
        else
        {
            _note.Update(SecretKind.Note, name, body);
            Project.Resort(_note);
        }

        Status = _shell.Save() ? "Saved" : "Not saved";
    }

    [RelayCommand]
    private void Close()
    {
        Commit();
        _vault.CloseNote(this);
    }

    [RelayCommand]
    private void CopyAll()
    {
        if (Body.Length == 0)
            return;
        _shell.Clipboard.Copy(Body);
        _shell.ShowToast("Copied the note. Clipboard clears in 30 seconds.");
    }

    [RelayCommand]
    private void Delete()
    {
        Commit();
        if (_note is not { } note)
        {
            _vault.CloseNote(this);
            return;
        }

        _shell.OpenSheet(new ConfirmSheet(_shell)
        {
            Title = $"Delete “{note.Name}”?",
            Message = $"It will be removed from {Project.Name}.",
            ConfirmText = "Delete",
            IsDestructive = true,
            OnConfirm = () =>
            {
                _saveTimer.Stop();
                _dirty = false;
                Project.Remove(note);
                _shell.Save();
                _vault.CloseNote(this);
            },
        });
    }

    internal static string TitleOrFirstLine(string title, string body)
    {
        var trimmed = title.Trim();
        if (trimmed.Length > 0)
            return trimmed;
        var firstLine = body.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return firstLine.Length <= 60 ? firstLine : firstLine[..60].TrimEnd() + "…";
    }
}
