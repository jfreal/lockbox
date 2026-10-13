using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Lockbox.Core.Crypto;

public sealed record KdfParameters(int MemoryKiB, int Iterations, int Parallelism)
{
    /// <summary>256 MiB, 3 passes: about 0.6 s on a modern PC, and the same cost for every guess an attacker makes.</summary>
    public static KdfParameters Default { get; } = new(256 * 1024, 3, 4);
}

/// <summary>Turns a passphrase into a 256-bit key with Argon2id.</summary>
public static class PassphraseKdf
{
    public const int SaltSize = 16;

    public static byte[] DeriveKey(string passphrase, byte[] salt, KdfParameters parameters)
    {
        // NFC so the same passphrase typed on another keyboard layout still matches.
        var passwordBytes = Encoding.UTF8.GetBytes(passphrase.Normalize(NormalizationForm.FormC));
        try
        {
            using var argon = new Argon2id(passwordBytes)
            {
                Salt = salt,
                MemorySize = parameters.MemoryKiB,
                Iterations = parameters.Iterations,
                DegreeOfParallelism = parameters.Parallelism,
            };
            return argon.GetBytes(Aead.KeySize);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
