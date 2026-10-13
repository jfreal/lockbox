using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace Lockbox.Core.Crypto;

/// <summary>A short, non-secret fingerprint of "this Windows user on this PC".</summary>
public static class DeviceIdentity
{
    public static string Current()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? "unknown";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{Environment.MachineName}|{sid}"));
        return Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }
}
