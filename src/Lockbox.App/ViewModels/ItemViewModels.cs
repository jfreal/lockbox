using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Lockbox.Core.Vault;

namespace Lockbox.App.ViewModels;

public sealed partial class ProjectViewModel : ObservableObject
{
    public ProjectViewModel(Project model)
    {
        Model = model;
        foreach (var secret in model.Secrets.OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase))
            Secrets.Add(new SecretViewModel(secret, this));
        Secrets.CollectionChanged += (_, _) => RefreshCounts();
    }

    public Project Model { get; }

    public ObservableCollection<SecretViewModel> Secrets { get; } = [];

    public string Name
    {
        get => Model.Name;
        set
        {
            if (Model.Name == value)
                return;
            Model.Name = value;
            Model.UpdatedAt = DateTimeOffset.UtcNow;
            OnPropertyChanged();
            foreach (var secret in Secrets)
                secret.RaiseProjectChanged();
        }
    }

    public int Count => Secrets.Count;

    public string CountText
    {
        get
        {
            var notes = Secrets.Count(s => s.IsNote);
            var keys = Secrets.Count - notes;
            var parts = new List<string>(2);
            if (keys > 0)
                parts.Add(keys == 1 ? "1 key" : $"{keys} keys");
            if (notes > 0)
                parts.Add(notes == 1 ? "1 note" : $"{notes} notes");
            return parts.Count == 0 ? "No secrets yet" : string.Join(" and ", parts);
        }
    }

    public bool HasSecrets => Secrets.Count > 0;

    public SecretViewModel Add(Secret secret)
    {
        Model.Secrets.Add(secret);
        Model.UpdatedAt = DateTimeOffset.UtcNow;
        var vm = new SecretViewModel(secret, this);
        InsertSorted(vm);
        return vm;
    }

    public void Remove(SecretViewModel secret)
    {
        Model.Secrets.Remove(secret.Model);
        Model.UpdatedAt = DateTimeOffset.UtcNow;
        Secrets.Remove(secret);
    }

    public void Resort(SecretViewModel secret)
    {
        Secrets.Remove(secret);
        InsertSorted(secret);
    }

    public void HideAll()
    {
        foreach (var secret in Secrets)
            secret.IsRevealed = false;
    }

    private void InsertSorted(SecretViewModel secret)
    {
        var index = 0;
        while (index < Secrets.Count && string.Compare(Secrets[index].Name, secret.Name, StringComparison.CurrentCultureIgnoreCase) <= 0)
            index++;
        Secrets.Insert(index, secret);
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(HasSecrets));
    }
}

public sealed partial class SecretViewModel(Secret model, ProjectViewModel project) : ObservableObject
{
    private const string Mask = "••••••••••••";

    public Secret Model { get; } = model;

    public ProjectViewModel Project { get; } = project;

    public string ProjectName => Project.Name;

    /// <summary>Search results show which project each secret belongs to.</summary>
    public bool ShowProject { get; init; }

    public bool IsNote => Model.Kind == SecretKind.Note;

    public string Glyph => IsNote ? "" : "";

    public string Name => Model.Name;

    public string Value => Model.Value;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayValue), nameof(RevealGlyph), nameof(RevealTip))]
    public partial bool IsRevealed { get; set; }

    public string DisplayValue
    {
        get
        {
            if (IsRevealed)
                return Model.Value.Length == 0 ? "(empty)" : Model.Value;
            if (!IsNote)
                return Mask;
            var lines = Model.Value.Split('\n').Length;
            return lines == 1 ? "Note · 1 line" : $"Note · {lines} lines";
        }
    }

    public string RevealGlyph => IsRevealed ? "" : "";

    public string RevealTip => IsRevealed ? "Hide" : "Show";

    public string CopyTip => IsNote ? "Copy note" : "Copy value";

    public SecretViewModel ForSearch() => new(Model, Project) { ShowProject = true };

    public void Update(SecretKind kind, string name, string value)
    {
        Model.Kind = kind;
        Model.Name = name;
        Model.Value = value;
        Model.UpdatedAt = DateTimeOffset.UtcNow;
        OnPropertyChanged(string.Empty);
    }

    public void RaiseProjectChanged() => OnPropertyChanged(nameof(ProjectName));
}
