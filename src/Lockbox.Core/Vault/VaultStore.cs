using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lockbox.Core.Crypto;

namespace Lockbox.Core.Vault;

/// <summary>Creates and opens the vault file. Everything after unlock goes through <see cref="UnlockedVault"/>.</summary>
public sealed class VaultStore(string vaultPath, KdfParameters kdf)
{
    internal static readonly byte[] PassphraseSlotAad = Encoding.ASCII.GetBytes("lockbox/v1/slot/passphrase");
        internal static readonly byte[] PayloadAad = Encoding.ASCII.GetBytes("lockbox/v1/payload");

    public string VaultPath { get; } = vaultPath;

    internal KdfParameters Kdf { get; } = kdf;

    public bool Exists => File.Exists(VaultPath);

    public VaultEnvelope ReadEnvelope() => VaultEnvelope.Parse(File.ReadAllBytes(VaultPath));

    /// <summary>True when this vault has a Windows Hello slot that was enrolled by this user on this PC.</summary>
    public bool HasHelloForThisDevice()
    {
        var hello = ReadEnvelope().Hello;
        return hello is not null && hello.DeviceId == DeviceIdentity.Current();
    }

    public UnlockedVault Create(string passphrase)
    {
        if (Exists)
            throw new InvalidOperationException("A vault already exists here.");

        var masterKey = RandomNumberGenerator.GetBytes(Aead.KeySize);
        var envelope = new VaultEnvelope { Passphrase = WrapWithPassphrase(masterKey, passphrase, Kdf) };
        var vault = new UnlockedVault(this, envelope, masterKey, new VaultData());
        vault.Save();
        return vault;
    }

    public UnlockedVault UnlockWithPassphrase(string passphrase)
    {
        var envelope = ReadEnvelope();
        var masterKey = UnwrapWithPassphrase(envelope.Passphrase, passphrase) ?? throw new WrongPassphraseException();
        return Open(envelope, masterKey);
    }

    /// <param name="unwrap">Given the Hello key name and the wrapped master key, asks Windows Hello to decrypt it. Shows the Hello prompt.</param>
    public async Task<UnlockedVault> UnlockWithHelloAsync(Func<string, byte[], Task<byte[]>> unwrap)
    {
        var envelope = ReadEnvelope();
        var slot = envelope.Hello ?? throw new HelloUnlockException("Windows Hello isn't set up for this vault.");
        byte[] wrapped;
        try
        {
            wrapped = Dpapi.Unprotect(slot.ProtectedKey);
        }
        catch (CryptographicException ex)
        {
            throw new HelloUnlockException("Windows Hello was set up on a different PC or Windows account. Use your passphrase.", ex);
        }

        var masterKey = await unwrap(slot.KeyName, wrapped);
        if (masterKey.Length != Aead.KeySize)
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw new HelloUnlockException("Windows Hello returned an unexpected key. Use your passphrase, then turn Windows Hello on again in Settings.");
        }

        try
        {
            return Open(envelope, masterKey);
        }
        catch (InvalidVaultFileException ex)
        {
            throw new HelloUnlockException("Windows Hello's key doesn't match this vault. Use your passphrase, then turn Windows Hello on again in Settings.", ex);
        }
    }

    private UnlockedVault Open(VaultEnvelope envelope, byte[] masterKey)
    {
        byte[] json;
        try
        {
            json = Aead.Decrypt(masterKey, envelope.Payload.Nonce, envelope.Payload.Ciphertext, PayloadAad);
        }
        catch (CryptographicException ex)
        {
            CryptographicOperations.ZeroMemory(masterKey);
            throw new InvalidVaultFileException("The vault file is damaged or was changed outside Lockbox.", ex);
        }

        try
        {
            var data = JsonSerializer.Deserialize<VaultData>(json, VaultEnvelope.JsonOptions) ?? new VaultData();
            return new UnlockedVault(this, envelope, masterKey, data);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }
    }

    /// <returns>The master key, or null when the passphrase is wrong.</returns>
    internal static byte[]? UnwrapWithPassphrase(PassphraseSlot slot, string passphrase)
    {
        var kek = PassphraseKdf.DeriveKey(passphrase, slot.Salt, new KdfParameters(slot.MemoryKiB, slot.Iterations, slot.Parallelism));
        try
        {
            return Aead.Decrypt(kek, slot.Nonce, slot.WrappedKey, PassphraseSlotAad);
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    internal static PassphraseSlot WrapWithPassphrase(byte[] masterKey, string passphrase, KdfParameters kdf)
    {
        var salt = RandomNumberGenerator.GetBytes(PassphraseKdf.SaltSize);
        var kek = PassphraseKdf.DeriveKey(passphrase, salt, kdf);
        try
        {
            var (nonce, wrapped) = Aead.Encrypt(kek, masterKey, PassphraseSlotAad);
            return new PassphraseSlot
            {
                Salt = salt,
                MemoryKiB = kdf.MemoryKiB,
                Iterations = kdf.Iterations,
                Parallelism = kdf.Parallelism,
                Nonce = nonce,
                WrappedKey = wrapped,
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    internal void Write(VaultEnvelope envelope)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(VaultPath)!);
        FileUtil.WriteAtomically(VaultPath, envelope.ToBytes());
    }
}
