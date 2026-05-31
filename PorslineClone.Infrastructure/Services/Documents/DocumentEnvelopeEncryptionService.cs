using System.Security.Cryptography;

namespace PorslineClone.Infrastructure.Services.Documents;

/// <summary>AES-256-GCM envelope encryption: per-file DEK, DEK wrapped with KEK.</summary>
public sealed class DocumentEnvelopeEncryptionService(DocumentMasterKeyProvider masterKeys)
{
    public const string AlgorithmName = "AES-256-GCM";
    private const int DekSizeBytes = 32;
    private const int NonceSizeBytes = 12;
    private const int TagSizeBytes = 16;

    public bool IsEncryptionActive => masterKeys.EncryptionEnabled;

    public string PrimaryKeyId => masterKeys.PrimaryKeyId;

    public EncryptedDocumentPayload Encrypt(byte[] plaintext, string? keyId = null)
    {
        var kekId = string.IsNullOrWhiteSpace(keyId) ? masterKeys.PrimaryKeyId : keyId.Trim();
        var kek = masterKeys.GetKey(kekId);

        var dek = RandomNumberGenerator.GetBytes(DekSizeBytes);
        var fileNonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);

        var ciphertext = new byte[plaintext.Length];
        var fileTag = new byte[TagSizeBytes];
        using (var fileCipher = new AesGcm(dek, TagSizeBytes))
            fileCipher.Encrypt(fileNonce, plaintext, ciphertext, fileTag);

        var encryptedDek = WrapDek(dek, kek);

        return new EncryptedDocumentPayload(
            kekId,
            Convert.ToBase64String(fileNonce),
            Convert.ToBase64String(encryptedDek),
            ciphertext,
            fileTag);
    }

    public byte[] Decrypt(
        byte[] ciphertext,
        byte[] fileTag,
        string fileNonceBase64,
        string encryptedDekBase64,
        string encryptionKeyId)
    {
        var kek = masterKeys.GetKey(encryptionKeyId);
        var dek = UnwrapDek(Convert.FromBase64String(encryptedDekBase64), kek);
        var fileNonce = Convert.FromBase64String(fileNonceBase64);
        if (fileNonce.Length != NonceSizeBytes)
            throw new CryptographicException("Invalid file nonce length.");

        var plaintext = new byte[ciphertext.Length];
        using (var fileCipher = new AesGcm(dek, TagSizeBytes))
            fileCipher.Decrypt(fileNonce, ciphertext, fileTag, plaintext);

        CryptographicOperations.ZeroMemory(dek);
        return plaintext;
    }

    /// <summary>DEK را با KEK جدید می‌پیچد (چرخش کلید بدون بازنویسی فایل).</summary>
    public string RewrapDek(string encryptedDekBase64, string currentKeyId, string newKeyId)
    {
        if (string.Equals(currentKeyId, newKeyId, StringComparison.OrdinalIgnoreCase))
            return encryptedDekBase64;

        var dek = UnwrapDek(Convert.FromBase64String(encryptedDekBase64), masterKeys.GetKey(currentKeyId));
        try
        {
            return Convert.ToBase64String(WrapDek(dek, masterKeys.GetKey(newKeyId)));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    private static byte[] WrapDek(byte[] dek, byte[] kek)
    {
        var wrapNonce = RandomNumberGenerator.GetBytes(NonceSizeBytes);
        var wrapped = new byte[DekSizeBytes];
        var wrapTag = new byte[TagSizeBytes];
        using (var wrapCipher = new AesGcm(kek, TagSizeBytes))
            wrapCipher.Encrypt(wrapNonce, dek, wrapped, wrapTag);

        var payload = new byte[NonceSizeBytes + DekSizeBytes + TagSizeBytes];
        Buffer.BlockCopy(wrapNonce, 0, payload, 0, NonceSizeBytes);
        Buffer.BlockCopy(wrapped, 0, payload, NonceSizeBytes, DekSizeBytes);
        Buffer.BlockCopy(wrapTag, 0, payload, NonceSizeBytes + DekSizeBytes, TagSizeBytes);
        return payload;
    }

    private static byte[] UnwrapDek(byte[] payload, byte[] kek)
    {
        if (payload.Length != NonceSizeBytes + DekSizeBytes + TagSizeBytes)
            throw new CryptographicException("Invalid encrypted DEK payload length.");

        var wrapNonce = payload.AsSpan(0, NonceSizeBytes);
        var wrapped = payload.AsSpan(NonceSizeBytes, DekSizeBytes);
        var wrapTag = payload.AsSpan(NonceSizeBytes + DekSizeBytes, TagSizeBytes);

        var dek = new byte[DekSizeBytes];
        using (var wrapCipher = new AesGcm(kek, TagSizeBytes))
            wrapCipher.Decrypt(wrapNonce, wrapped, wrapTag, dek);
        return dek;
    }
}

public sealed record EncryptedDocumentPayload(
    string EncryptionKeyId,
    string FileNonceBase64,
    string EncryptedDekBase64,
    byte[] Ciphertext,
    byte[] Tag);
