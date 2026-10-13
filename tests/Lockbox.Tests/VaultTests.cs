using System.Security.Cryptography;
using System.Text;
using Lockbox.Core.Crypto;
using Lockbox.Core.Vault;

namespace Lockbox.Tests;

public sealed class VaultTests : IDisposable
{
    // Small Argon2 settings so the suite stays fast. Production uses KdfParameters.Default.
    private static readonly KdfParameters FastKdf = new(1024, 1, 1);
    private const string Passphrase = "correct horse battery staple orbit";

    private readonly string _dir = Directory.CreateTempSubdirectory("lockbox-tests-").FullName;

    private string VaultPath => Path.Combine(_dir, "vault.lockbox");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private VaultStore NewStore() => new(VaultPath, FastKdf);

    private static UnlockedVault AddSample(UnlockedVault vault)
    {
        vault.Data.Projects.Add(new Project
        {
            Name = "acme-web",
            Secrets =
            {
                new Secret { Name = "NETLIFY_TOKEN", Value = "nfp_abc123" },
                new Secret { Kind = SecretKind.Note, Name = "Recovery codes", Value = "1111\n2222" },
            },
        });
        vault.Save();
        return vault;
    }

    [Fact]
    public void Passphrase_round_trip_restores_projects_and_secrets()
    {
        using (var vault = NewStore().Create(Passphrase))
            AddSample(vault);

        using var reopened = NewStore().UnlockWithPassphrase(Passphrase);
        var project = Assert.Single(reopened.Data.Projects);
        Assert.Equal("acme-web", project.Name);
        Assert.Equal("nfp_abc123", project.Secrets[0].Value);
        Assert.Equal(SecretKind.Note, project.Secrets[1].Kind);
    }

    [Fact]
    public void Wrong_passphrase_is_rejected()
    {
        using (NewStore().Create(Passphrase)) { }

        Assert.Throws<WrongPassphraseException>(() => NewStore().UnlockWithPassphrase(Passphrase + "x"));
    }

    [Fact]
    public void File_on_disk_contains_no_plaintext()
    {
        using (var vault = NewStore().Create(Passphrase))
            AddSample(vault);

        var text = File.ReadAllText(VaultPath);
        Assert.DoesNotContain("nfp_abc123", text);
        Assert.DoesNotContain("acme-web", text);
        Assert.DoesNotContain("NETLIFY_TOKEN", text);
    }

    [Fact]
    public void Tampered_payload_is_detected()
    {
        using (var vault = NewStore().Create(Passphrase))
            AddSample(vault);

        var envelope = VaultEnvelope.Parse(File.ReadAllBytes(VaultPath));
        envelope.Payload.Ciphertext[5] ^= 0x01;
        File.WriteAllBytes(VaultPath, envelope.ToBytes());

        Assert.Throws<InvalidVaultFileException>(() => NewStore().UnlockWithPassphrase(Passphrase));
    }

    [Fact]
    public void Change_passphrase_keeps_data_and_retires_old_passphrase()
    {
        using (var vault = AddSample(NewStore().Create(Passphrase)))
            vault.ChangePassphrase(Passphrase, "new passphrase with five words");

        Assert.Throws<WrongPassphraseException>(() => NewStore().UnlockWithPassphrase(Passphrase));
        using var reopened = NewStore().UnlockWithPassphrase("new passphrase with five words");
        Assert.Single(reopened.Data.Projects);
    }

    [Fact]
    public void Change_passphrase_requires_current_passphrase()
    {
        using var vault = NewStore().Create(Passphrase);
        Assert.Throws<WrongPassphraseException>(() => vault.ChangePassphrase("not it", "whatever new one"));
    }

    // Stands in for the Windows Hello (Passport) key: same RSA PKCS#1 v1.5 wrap, minus the TPM and prompt.
    private static readonly RSA HelloKey = RSA.Create(2048);
    private const string HelloKeyName = "Lockbox/Test/Key";

    private static byte[] HelloWrap(byte[] key) => HelloKey.Encrypt(key, RSAEncryptionPadding.Pkcs1);

    private static Task<byte[]> HelloUnwrap(string keyName, byte[] wrapped)
    {
        Assert.Equal(HelloKeyName, keyName);
        return Task.FromResult(HelloKey.Decrypt(wrapped, RSAEncryptionPadding.Pkcs1));
    }

    [Fact]
    public async Task Hello_slot_unlocks_and_rejects_a_wrong_key()
    {
        using (var vault = AddSample(NewStore().Create(Passphrase)))
        {
            await vault.EnableHelloAsync(HelloKeyName, HelloWrap, HelloUnwrap);
            Assert.True(vault.HasHelloForThisDevice);
        }

        var store = NewStore();
        Assert.True(store.HasHelloForThisDevice());
        Assert.Equal(HelloKeyName, store.ReadEnvelope().Hello!.KeyName);

        using (var viaHello = await store.UnlockWithHelloAsync(HelloUnwrap))
            Assert.Equal("acme-web", viaHello.Data.Projects[0].Name);

        // Hello unlock is repeatable: decryption is deterministic.
        using (var again = await store.UnlockWithHelloAsync(HelloUnwrap))
            Assert.Single(again.Data.Projects);

        await Assert.ThrowsAsync<HelloUnlockException>(() =>
            store.UnlockWithHelloAsync((_, _) => Task.FromResult(RandomNumberGenerator.GetBytes(Aead.KeySize))));
        await Assert.ThrowsAsync<HelloUnlockException>(() =>
            store.UnlockWithHelloAsync((_, _) => Task.FromResult(new byte[5])));
    }

    [Fact]
    public async Task Hello_is_not_saved_when_the_round_trip_fails()
    {
        using (var vault = NewStore().Create(Passphrase))
        {
            await Assert.ThrowsAsync<HelloUnlockException>(() =>
                vault.EnableHelloAsync(HelloKeyName, HelloWrap, (_, _) => Task.FromResult(RandomNumberGenerator.GetBytes(Aead.KeySize))));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                vault.EnableHelloAsync(HelloKeyName, HelloWrap, (_, _) => throw new InvalidOperationException("Hello failed")));
            Assert.False(vault.HasHelloForThisDevice);
        }

        Assert.Null(NewStore().ReadEnvelope().Hello);
    }

    [Fact]
    public async Task Hello_slot_from_another_device_is_not_offered()
    {
        using (var vault = NewStore().Create(Passphrase))
            await vault.EnableHelloAsync(HelloKeyName, HelloWrap, HelloUnwrap);

        var envelope = VaultEnvelope.Parse(File.ReadAllBytes(VaultPath));
        envelope.Hello!.DeviceId = "someone-elses-pc";
        File.WriteAllBytes(VaultPath, envelope.ToBytes());

        Assert.False(NewStore().HasHelloForThisDevice());
    }

    [Fact]
    public async Task Passphrase_still_works_after_hello_is_enabled_and_disabled()
    {
        using (var vault = NewStore().Create(Passphrase))
        {
            await vault.EnableHelloAsync(HelloKeyName, HelloWrap, HelloUnwrap);
            vault.DisableHello();
        }

        Assert.Null(NewStore().ReadEnvelope().Hello);
        using var reopened = NewStore().UnlockWithPassphrase(Passphrase);
    }

    [Fact]
    public async Task Hello_slot_is_wrapped_by_dpapi_and_holds_no_raw_key()
    {
        using (var vault = NewStore().Create(Passphrase))
            await vault.EnableHelloAsync(HelloKeyName, HelloWrap, HelloUnwrap);

        var slot = NewStore().ReadEnvelope().Hello!;
        // Unprotect succeeds only for this Windows user; inside is the 256-byte RSA-2048 ciphertext.
        Assert.Equal(256, Dpapi.Unprotect(slot.ProtectedKey).Length);
    }

    [Fact]
    public void Non_vault_file_is_rejected_with_a_clear_message()
    {
        File.WriteAllText(VaultPath, "{\"hello\": 1}");
        var ex = Assert.Throws<InvalidVaultFileException>(() => NewStore().ReadEnvelope());
        Assert.Contains("not a Lockbox vault", ex.Message);
    }

    [Fact]
    public void Passphrase_is_unicode_normalized()
    {
        const string composed = "café latte orbit river stone";
        const string decomposed = "café latte orbit river stone";
        using (NewStore().Create(composed)) { }

        using var reopened = NewStore().UnlockWithPassphrase(decomposed);
    }

    [Fact]
    public void Default_kdf_is_strong()
    {
        Assert.True(KdfParameters.Default.MemoryKiB >= 64 * 1024);
        Assert.True(KdfParameters.Default.Iterations >= 3);
    }

    [Fact]
    public void Aead_rejects_wrong_associated_data()
    {
        var key = RandomNumberGenerator.GetBytes(Aead.KeySize);
        var (nonce, ct) = Aead.Encrypt(key, Encoding.UTF8.GetBytes("hi"), "a"u8);
        Assert.ThrowsAny<CryptographicException>(() => Aead.Decrypt(key, nonce, ct, "b"u8));
    }
}
