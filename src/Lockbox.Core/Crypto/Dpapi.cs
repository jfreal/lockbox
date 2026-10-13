using System.Security.Cryptography;
using System.Text;

namespace Lockbox.Core.Crypto;

/// <summary>Windows DPAPI, scoped to the signed-in Windows user.</summary>
public static class Dpapi
{
    private static readonly byte[] Entropy = Encoding.ASCII.GetBytes("lockbox/v1/dpapi");

    public static byte[] Protect(byte[] data) =>
        ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);

    /// <exception cref="CryptographicException">Different Windows user or PC, or the blob is damaged.</exception>
    public static byte[] Unprotect(byte[] data) =>
        ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
}
