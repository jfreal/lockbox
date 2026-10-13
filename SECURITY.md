# Security

## Reporting a problem

Please report vulnerabilities privately through GitHub. Open the repository's **Security** tab and choose **Report a vulnerability**. Don't open a public issue for a security bug.

Include the Lockbox commit you tested, your Windows version and steps to reproduce. I'll reply in the advisory thread. This is a one-person project, so there's no response-time promise.

## What Lockbox protects against

- **A stolen or lost PC that is off or locked.** The vault on disk is ciphertext. Opening it needs your passphrase, or your Windows account plus a Windows Hello check on that same PC.
- **A leaked backup.** Copies in Dropbox or anywhere else only open with the passphrase. Argon2id with 256 MiB of memory makes each passphrase guess slow and memory-hungry.
- **Other Windows accounts on the same PC.** The Windows Hello slot is wrapped with DPAPI for your account.
- **Someone editing the vault file.** AES-GCM authenticates the payload, so any change makes unlock fail instead of loading altered data.
- **Clipboard leaks.** Copied values carry the Windows flags that keep them out of clipboard history and cloud clipboard, and Lockbox clears the clipboard 30 seconds later if the value is still there.
- **Screenshots and screen sharing.** The window sets `WDA_EXCLUDEFROMCAPTURE`, so it appears blank in captures.

## What it does not protect against

- **Malware running as you while the vault is unlocked.** It can read the app's memory, log keystrokes or read the clipboard during those 30 seconds.
- **Malware running as you while the vault is locked**, if you approve a Windows Hello prompt it triggers. Lockbox sets its own window as the prompt's owner and labels it "Unlock Lockbox", but Windows can't prove which program asked.
- **A weak passphrase.** Backups are exactly as strong as the passphrase. Lockbox requires 16 characters and suggests five or more random words.
- **Someone who has your passphrase**, or your Windows password and your Hello PIN.

## Design

### Vault file

The vault is one JSON file. Byte fields are base64.

| Field | Contents |
|---|---|
| `format`, `version` | `lockbox-vault`, `1` |
| `passphrase` | Argon2id parameters, 16-byte salt, nonce, wrapped master key |
| `hello` | Optional. Device ID, Hello key name, DPAPI-protected wrapped master key |
| `payload` | Nonce and AES-256-GCM ciphertext of the projects and secrets |

A random 256-bit master key encrypts the payload. Each save uses a fresh random 96-bit nonce. Each AES-GCM operation binds a fixed label as associated data: `lockbox/v1/payload`, `lockbox/v1/slot/passphrase`. A wrapped key therefore can't be moved into another role in the file.

Writes go to a temporary file that is flushed to disk and then renamed over the vault, so a crash mid-save leaves the old vault intact.

### Passphrase slot

- Argon2id, 256 MiB memory, 3 passes, 4 lanes, 16-byte random salt, 32-byte output. About 0.6 seconds per attempt on a current desktop.
- The passphrase is Unicode NFC-normalized before hashing, so the same words typed on another keyboard layout still match.
- The derived key wraps the master key with AES-256-GCM.
- Changing the passphrase rewraps the same master key with a new salt. Older daily backups still need the old passphrase.

### Windows Hello slot

Each Windows Hello user has a default RSA-2048 decryption key in the Microsoft Passport key storage provider. The TPM holds it, and decrypting with it needs a Hello gesture.

1. Lockbox reads that key's public half and encrypts the master key to it with RSA PKCS#1 v1.5 padding. No prompt.
2. It wraps the result with DPAPI for the current user, with a fixed entropy value.
3. To unlock, it removes the DPAPI layer and asks the provider to decrypt. Windows shows the Hello prompt over the Lockbox window, with the gesture-required flag set.
4. When you turn Hello on, Lockbox runs step 3 once and checks that the key it gets back matches. It saves the slot only if that works.

The slot records a device ID, a truncated SHA-256 of the machine name and the user's SID. Lockbox only offers Hello when the ID matches, so a restored backup falls back to the passphrase.

Why decryption and not a signature? Some password managers derive a key from a Windows Hello signature over a fixed challenge. That only works if the signature is identical every time. Microsoft documents Hello signatures as RSA-PSS, which mixes in random salt, so that approach can break. Decryption with a fixed key always returns the same plaintext.

PKCS#1 v1.5 encryption padding is what the Passport provider accepts. The usual attack on it needs an oracle that reports padding errors on attacker-chosen ciphertexts. Here the only ciphertext is Lockbox's own, it never leaves the PC, and every decryption costs the user a Hello gesture.

Lockbox never creates or deletes Windows Hello keys. Turning Hello off removes the slot from the vault and leaves your Windows Hello setup alone.

### While the app is open

- The master key lives in a byte array that Lockbox zeroes on lock and exit.
- Auto-lock fires after 5 minutes without keyboard or mouse input in the app, on Windows session lock, on remote disconnect and before sleep.
- Every 14 days the unlock screen asks for the passphrase instead of Windows Hello.
- The app has no network code and makes no network requests.
- Only one instance runs at a time.

### Dependencies

| Package | Use |
|---|---|
| .NET `System.Security.Cryptography` | AES-GCM, RSA, random numbers |
| `System.Security.Cryptography.ProtectedData` | DPAPI |
| `Konscious.Security.Cryptography.Argon2` | Argon2id |
| `CommunityToolkit.Mvvm` | UI plumbing, no crypto |

## Known gaps

- No independent audit.
- .NET strings can't be reliably wiped. Revealed values, values typed into editors and imported text can stay in process memory until Lockbox exits, and Windows may page that memory to disk.
- On some PCs Windows can't reach the default Hello decryption key and returns `0x80090011` or `0xD0000225`. Lockbox then refuses to turn Hello on and you keep using the passphrase.
- Release builds aren't code-signed. Build from source with `install.ps1` if you want to know exactly what you're running.
