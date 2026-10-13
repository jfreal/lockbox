using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lockbox.Core.Vault;

/// <summary>
/// The on-disk file. One random master key encrypts the payload. The master key is stored twice,
/// once wrapped by the passphrase and once encrypted to a Windows Hello key, then DPAPI ("key slots").
/// Byte arrays serialize as base64.
/// </summary>
public sealed class VaultEnvelope
{
    public const string FormatName = "lockbox-vault";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = FormatName;
    public int Version { get; set; } = CurrentVersion;
    public PassphraseSlot Passphrase { get; set; } = new();
    public HelloSlot? Hello { get; set; }
    public SealedBlob Payload { get; set; } = new();

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static VaultEnvelope Parse(byte[] json)
    {
        VaultEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<VaultEnvelope>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidVaultFileException("This is not a Lockbox vault file.", ex);
        }

        if (envelope is null || envelope.Format != FormatName)
            throw new InvalidVaultFileException("This is not a Lockbox vault file.");
        if (envelope.Version != CurrentVersion)
            throw new InvalidVaultFileException($"This vault uses format version {envelope.Version}, which this version of Lockbox can't read.");
        return envelope;
    }

    public byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, JsonOptions);
}

public sealed class PassphraseSlot
{
    public string Kdf { get; set; } = "argon2id";
    public byte[] Salt { get; set; } = [];
    public int MemoryKiB { get; set; }
    public int Iterations { get; set; }
    public int Parallelism { get; set; }
    public byte[] Nonce { get; set; } = [];
    public byte[] WrappedKey { get; set; } = [];
}

public sealed class HelloSlot
{
    /// <summary>Which Windows user on which PC enrolled this slot. Not secret.</summary>
    public string DeviceId { get; set; } = "";
    /// <summary>Name of the Windows Hello (Passport) key that can decrypt the master key.</summary>
    public string KeyName { get; set; } = "";
    /// <summary>DPAPI( RSA PKCS#1 v1.5 encryption of the master key to the Hello key's public half ).</summary>
    public byte[] ProtectedKey { get; set; } = [];
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SealedBlob
{
    public byte[] Nonce { get; set; } = [];
    public byte[] Ciphertext { get; set; } = [];
}

public sealed class InvalidVaultFileException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class WrongPassphraseException() : Exception("That passphrase didn't unlock the vault.");

public sealed class HelloUnlockException(string message, Exception? inner = null) : Exception(message, inner);
