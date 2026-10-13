namespace Lockbox.Core.Vault;

/// <summary>Everything inside the encrypted payload.</summary>
public sealed class VaultData
{
    public List<Project> Projects { get; set; } = [];
}

public sealed class Project
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public List<Secret> Secrets { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public enum SecretKind
{
    KeyValue,
    Note,
}

public sealed class Secret
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public SecretKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
