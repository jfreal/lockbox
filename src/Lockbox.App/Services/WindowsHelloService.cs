using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;
using Windows.Security.Credentials;

namespace Lockbox.App.Services;

public sealed class HelloCanceledException() : Exception("Windows Hello was canceled.");

/// <summary>
/// Windows Hello through the Microsoft Passport key storage provider. Every Windows Hello user has a
/// default RSA-2048 decryption key, held by the TPM, that needs your PIN, face, or fingerprint to
/// decrypt. Lockbox encrypts the vault's master key to it (public-key operation, no prompt) and asks
/// Hello to decrypt it on unlock (prompt). Decryption is deterministic, unlike Hello signatures
/// (RSA-PSS, randomized). The prompt is parented to Lockbox's window.
///
/// This is the KeePassWinHello plugin's default ("local") mode. Creating a separate persisted key
/// fails in NCryptFinalizeKey on this machine (NTE_NOT_FOUND) and has open bug reports upstream.
/// Lockbox never creates or deletes Hello keys.
/// </summary>
public sealed class WindowsHelloService
{
    /// <summary>
    /// True when Lockbox's own Hello path works up to the prompt: Windows Hello is set up, the default
    /// decryption key exists, and its public half can be read. No prompt.
    /// </summary>
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            if (!await KeyCredentialManager.IsSupportedAsync())
                return false;
            return await Task.Run(() =>
            {
                Wrap(new byte[32], DefaultKeyName());
                return true;
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The full name of this Windows user's default Hello decryption key.</summary>
    public string DefaultKeyName()
    {
        var sid = WindowsIdentity.GetCurrent().User?.Value ?? throw new InvalidOperationException("No Windows user SID.");
        var status = NCrypt.NgcGetDefaultDecryptionKeyName(sid, 0, 0, out var namePointer);
        // The provider owns this string; it is tiny and read once, so it is not freed.
        var name = status == 0 && namePointer != 0 ? Marshal.PtrToStringUni(namePointer) : null;
        if (string.IsNullOrEmpty(name))
            throw new InvalidOperationException($"Windows Hello has no decryption key for this account (0x{status:X8}). Set up a Windows Hello PIN in Windows Settings first.");
        return name;
    }

    /// <summary>
    /// Encrypts to the Hello key's public half in software. No prompt. (The Passport provider refuses
    /// NCryptEncrypt on this key with STATUS_NOT_FOUND, but exports the public key.)
    /// </summary>
    public byte[] Wrap(byte[] secret, string keyName)
    {
        using var provider = OpenProvider();
        using var key = OpenKey(provider, keyName, NCrypt.SilentFlag);
        Check(NCrypt.NCryptExportKey(key, 0, NCrypt.RsaPublicBlob, 0, null, 0, out var size, 0), "measure public key");
        var blob = new byte[size];
        Check(NCrypt.NCryptExportKey(key, 0, NCrypt.RsaPublicBlob, 0, blob, blob.Length, out size, 0), "export public key");

        using var publicKey = CngKey.Import(blob[..size], new CngKeyBlobFormat(NCrypt.RsaPublicBlob));
        using var rsa = new RSACng(publicKey);
        return rsa.Encrypt(secret, RSAEncryptionPadding.Pkcs1);
    }

    /// <summary>Decrypts with the Hello key. Shows the Windows Hello prompt over <paramref name="ownerWindow"/>.</summary>
    public Task<byte[]> UnwrapAsync(byte[] wrapped, nint ownerWindow, string keyName) => Task.Run(() =>
    {
        using var provider = OpenProvider();
        using var key = OpenKey(provider, keyName, 0);
        SetPrompt(key, ownerWindow, "Unlock Lockbox");
        SetInt(key, "PinCacheIsGestureRequired", 1);
        var output = new byte[wrapped.Length];
        Check(NCrypt.NCryptDecrypt(key, wrapped, wrapped.Length, 0, output, output.Length, out var size, NCrypt.PadPkcs1Flag), "decrypt");
        var result = output[..size];
        CryptographicOperations.ZeroMemory(output);
        return result;
    });

    private static SafeNCryptProviderHandle OpenProvider()
    {
        Check(NCrypt.NCryptOpenStorageProvider(out var provider, "Microsoft Passport Key Storage Provider", 0), "open Windows Hello key storage");
        return provider;
    }

    private static SafeNCryptKeyHandle OpenKey(SafeNCryptProviderHandle provider, string keyName, int flags)
    {
        var status = NCrypt.NCryptOpenKey(provider, out var key, keyName, 0, flags);
        if (status != 0)
            key.Dispose();
        Check(status, "open key");
        return key;
    }

    private static void SetPrompt(SafeNCryptKeyHandle key, nint ownerWindow, string message)
    {
        if (ownerWindow != 0)
        {
            var handle = IntPtr.Size == 8 ? BitConverter.GetBytes((long)ownerWindow) : BitConverter.GetBytes((int)ownerWindow);
            Check(NCrypt.SetProperty(key, "HWND Handle", handle), "set prompt window");
        }

        var text = System.Text.Encoding.Unicode.GetBytes(message + "\0");
        Check(NCrypt.SetProperty(key, "Use Context", text), "set prompt text");
    }

    private static void SetInt(SafeNCryptKeyHandle key, string property, int value) =>
        Check(NCrypt.SetProperty(key, property, BitConverter.GetBytes(value)), $"set {property}");

    private static void Check(int status, string step)
    {
        if (status == 0)
            return;
        if (status is NCrypt.UserCancelled or NCrypt.ErrorCancelled)
            throw new HelloCanceledException();
        if (status is NCrypt.NoKey or NCrypt.BadKeyset or NCrypt.NotFound or NCrypt.StatusNotFound)
            throw new CryptographicException($"Windows Hello couldn't reach its keys (0x{status:X8}). Resetting your Windows Hello PIN in Windows Settings usually fixes this. Your passphrase still works.") { HResult = status };
        throw new CryptographicException($"Windows Hello failed to {step} (0x{status:X8}).") { HResult = status };
    }

    private static class NCrypt
    {
        public const int SilentFlag = 0x40;
        public const int PadPkcs1Flag = 0x2;

        public const int UserCancelled = unchecked((int)0x80090036);
        public const int ErrorCancelled = unchecked((int)0x800704C7);
        public const int NoKey = unchecked((int)0x8009000D);
        public const int BadKeyset = unchecked((int)0x80090016);
        public const int NotFound = unchecked((int)0x80090011);
        public const int StatusNotFound = unchecked((int)0xD0000225);

        public static int SetProperty(SafeNCryptHandle handle, string property, byte[] value) =>
            NCryptSetProperty(handle, property, value, value.Length, 0);

        [DllImport("cryptngc.dll", CharSet = CharSet.Unicode)]
        public static extern int NgcGetDefaultDecryptionKeyName(string sid, int reserved1, int reserved2, out nint keyName);

        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        public static extern int NCryptOpenStorageProvider(out SafeNCryptProviderHandle provider, string providerName, int flags);

        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        public static extern int NCryptOpenKey(SafeNCryptProviderHandle provider, out SafeNCryptKeyHandle key, string keyName, int legacyKeySpec, int flags);

        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        public static extern int NCryptSetProperty(SafeNCryptHandle handle, string property, byte[] input, int inputSize, int flags);

        public const string RsaPublicBlob = "RSAPUBLICBLOB";

        [DllImport("ncrypt.dll", CharSet = CharSet.Unicode)]
        public static extern int NCryptExportKey(SafeNCryptKeyHandle key, nint exportKey, string blobType, nint parameters, byte[]? output, int outputSize, out int resultSize, int flags);

        [DllImport("ncrypt.dll")]
        public static extern int NCryptDecrypt(SafeNCryptKeyHandle key, byte[] input, int inputSize, nint paddingInfo, byte[] output, int outputSize, out int resultSize, int flags);
    }
}
