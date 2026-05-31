using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PorslineClone.Infrastructure.Options;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Tests;

public class DocumentEnvelopeEncryptionTests
{
    private static DocumentEnvelopeEncryptionService CreateService(string primaryId = "v1", params (string id, byte[] key)[] keys)
    {
        var dict = new Dictionary<string, string?>
        {
            ["DocumentEncryption:Enabled"] = "true",
            ["DocumentEncryption:PrimaryKeyId"] = primaryId,
        };
        foreach (var (id, key) in keys)
            dict[$"DOCUMENT_ENCRYPTION_KEY_{id}"] = Convert.ToBase64String(key);

        var config = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var opts = Options.Create(new DocumentEncryptionOptions { Enabled = true, PrimaryKeyId = primaryId });
        var provider = new DocumentMasterKeyProvider(config, opts, NullLogger<DocumentMasterKeyProvider>.Instance);
        return new DocumentEnvelopeEncryptionService(provider);
    }

    [Fact]
    public void EncryptDecrypt_roundtrip()
    {
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        var svc = CreateService("v1", ("v1", key));

        var plaintext = "Enterprise document payload 256-bit GCM test."u8.ToArray();
        var enc = svc.Encrypt(plaintext);

        var tag = enc.Tag;
        var decrypted = svc.Decrypt(enc.Ciphertext, tag, enc.FileNonceBase64, enc.EncryptedDekBase64, enc.EncryptionKeyId);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void RewrapDek_uses_new_kek_without_changing_plaintext()
    {
        var key1 = new byte[32];
        var key2 = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key1);
        System.Security.Cryptography.RandomNumberGenerator.Fill(key2);
        var svc = CreateService("v1", ("v1", key1), ("v2", key2));

        var plaintext = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var enc = svc.Encrypt(plaintext, "v1");
        var rewrapped = svc.RewrapDek(enc.EncryptedDekBase64, "v1", "v2");

        var svc2 = CreateService("v2", ("v1", key1), ("v2", key2));
        var decrypted = svc2.Decrypt(enc.Ciphertext, enc.Tag, enc.FileNonceBase64, rewrapped, "v2");
        Assert.Equal(plaintext, decrypted);
    }
}
