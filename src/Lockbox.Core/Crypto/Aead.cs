using System.Security.Cryptography;

namespace Lockbox.Core.Crypto;

/// <summary>AES-256-GCM with a fresh random 96-bit nonce per call.</summary>
public static class Aead
{
    public const int KeySize = 32;
    public const int NonceSize = 12;
    public const int TagSize = 16;

    public static (byte[] Nonce, byte[] Ciphertext) Encrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var output = new byte[plaintext.Length + TagSize];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Encrypt(nonce, plaintext, output.AsSpan(0, plaintext.Length), output.AsSpan(plaintext.Length), associatedData);
        return (nonce, output);
    }

    /// <exception cref="CryptographicException">Wrong key, or the data was changed.</exception>
    public static byte[] Decrypt(ReadOnlySpan<byte> key, ReadOnlySpan<byte> nonce, ReadOnlySpan<byte> ciphertext, ReadOnlySpan<byte> associatedData)
    {
        if (nonce.Length != NonceSize || ciphertext.Length < TagSize)
            throw new CryptographicException("Encrypted data is malformed.");

        var plaintext = new byte[ciphertext.Length - TagSize];
        using var gcm = new AesGcm(key, TagSize);
        gcm.Decrypt(nonce, ciphertext[..^TagSize], ciphertext[^TagSize..], plaintext, associatedData);
        return plaintext;
    }
}
