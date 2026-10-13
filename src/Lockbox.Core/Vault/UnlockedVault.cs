using System.Security.Cryptography;
using System.Text.Json;
using Lockbox.Core.Crypto;

namespace Lockbox.Core.Vault;

/// <summary>An open vault. Holds the master key in memory until <see cref="Dispose"/>.</summary>
public sealed class UnlockedVault : IDisposable
{
    private readonly VaultStore _store;
    private readonly VaultEnvelope _envelope;
    private byte[]? _masterKey;

    internal UnlockedVault(VaultStore store, VaultEnvelope envelope, byte[] masterKey, VaultData data)
    {
        _store = store;
        _envelope = envelope;
        _masterKey = masterKey;
        Data = data;
    }

    public VaultData Data { get; }

    public bool HasHelloForThisDevice => _envelope.Hello?.DeviceId == DeviceIdentity.Current();

    public string VaultPath => _store.VaultPath;

    private byte[] MasterKey => _masterKey ?? throw new ObjectDisposedException(nameof(UnlockedVault));

    public void Save()
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(Data, VaultEnvelope.JsonOptions);
        try
        {
            var (nonce, ciphertext) = Aead.Encrypt(MasterKey, json, VaultStore.PayloadAad);
            _envelope.Payload = new SealedBlob { Nonce = nonce, Ciphertext = ciphertext };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(json);
        }

        _store.Write(_envelope);
    }

    public bool VerifyPassphrase(string passphrase)
    {
        var key = VaultStore.UnwrapWithPassphrase(_envelope.Passphrase, passphrase);
        if (key is null)
            return false;
        CryptographicOperations.ZeroMemory(key);
        return true;
    }

    public void ChangePassphrase(string currentPassphrase, string newPassphrase)
    {
        if (!VerifyPassphrase(currentPassphrase))
            throw new WrongPassphraseException();
        _envelope.Passphrase = VaultStore.WrapWithPassphrase(MasterKey, newPassphrase, _store.Kdf);
        Save();
    }

    /// <summary>
    /// Adds the Windows Hello slot, but only after one real round trip proves Hello can give the
    /// master key back. If <paramref name="unwrap"/> throws or returns the wrong key, nothing is saved.
    /// </summary>
    /// <param name="keyName">The Windows Hello key the master key is encrypted to.</param>
    /// <param name="wrap">Encrypts to that key's public half. No prompt.</param>
    /// <param name="unwrap">Asks Windows Hello to decrypt. Shows the Hello prompt.</param>
    public async Task EnableHelloAsync(string keyName, Func<byte[], byte[]> wrap, Func<string, byte[], Task<byte[]>> unwrap)
    {
        var wrapped = wrap(MasterKey);
        var slot = new HelloSlot
        {
            DeviceId = DeviceIdentity.Current(),
            KeyName = keyName,
            ProtectedKey = Dpapi.Protect(wrapped),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var returned = await unwrap(keyName, Dpapi.Unprotect(slot.ProtectedKey));
        try
        {
            // Auto-lock can fire while the Hello prompt is open.
            if (_masterKey is null)
                throw new InvalidOperationException("Lockbox locked while Windows Hello was waiting. Unlock and try again.");
            if (!CryptographicOperations.FixedTimeEquals(returned, _masterKey))
                throw new HelloUnlockException("Windows Hello returned a different key. Windows Hello was not turned on.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(returned);
        }

        _envelope.Hello = slot;
        Save();
    }

    public void DisableHello()
    {
        _envelope.Hello = null;
        Save();
    }

    public void Dispose()
    {
        if (_masterKey is null)
            return;
        CryptographicOperations.ZeroMemory(_masterKey);
        _masterKey = null;
    }
}
