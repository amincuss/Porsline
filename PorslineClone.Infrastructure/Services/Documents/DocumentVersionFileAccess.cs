using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services.Documents;

public sealed class DocumentVersionFileAccess(
    DocumentFileStorageService storage,
    DocumentEnvelopeEncryptionService encryption) : IDocumentVersionFileAccess
{
    private const int TagSizeBytes = 16;

    public async Task<DocumentVersionSaveResult> SaveFromStreamAsync(
        Guid documentId,
        int versionNumber,
        Stream plaintext,
        string originalFileName,
        CancellationToken ct = default)
    {
        await using var ms = new MemoryStream();
        await plaintext.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        if (encryption.IsEncryptionActive)
        {
            var payload = encryption.Encrypt(bytes);
            var relative = await storage.SaveEncryptedBlobAsync(
                documentId,
                versionNumber,
                originalFileName,
                payload.Ciphertext,
                payload.Tag,
                ct);

            return new DocumentVersionSaveResult
            {
                RelativePath = relative.relativePath,
                OriginalFileName = relative.originalFileName,
                Extension = relative.extension,
                PlaintextSizeBytes = bytes.Length,
                IsEncrypted = true,
                EncryptionKeyId = payload.EncryptionKeyId,
                FileNonceBase64 = payload.FileNonceBase64,
                EncryptedDekBase64 = payload.EncryptedDekBase64,
            };
        }

        var plain = await storage.SavePlainBytesAsync(documentId, versionNumber, originalFileName, bytes, ct);
        return new DocumentVersionSaveResult
        {
            RelativePath = plain.relativePath,
            OriginalFileName = plain.originalFileName,
            Extension = plain.extension,
            PlaintextSizeBytes = bytes.Length,
            IsEncrypted = false,
        };
    }

    public async Task<DocumentVersionLocalFile> OpenLocalPathAsync(DocumentVersion version, CancellationToken ct = default)
    {
        var fullPath = storage.ResolveFullPath(version.StoredPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException("فایل فیزیکی یافت نشد", fullPath);

        if (!version.IsEncrypted)
            return new DocumentVersionLocalFile { Path = fullPath, DeleteWhenDisposed = false };

        if (string.IsNullOrWhiteSpace(version.FileNonceBase64)
            || string.IsNullOrWhiteSpace(version.EncryptedDekBase64)
            || string.IsNullOrWhiteSpace(version.EncryptionKeyId))
            throw new InvalidOperationException("Encryption metadata is incomplete for this version.");

        var encrypted = await File.ReadAllBytesAsync(fullPath, ct);
        if (encrypted.Length < TagSizeBytes)
            throw new CryptographicException("Encrypted file is too short.");

        var tag = encrypted[^TagSizeBytes..];
        var ciphertext = encrypted[..^TagSizeBytes];

        var plaintext = encryption.Decrypt(
            ciphertext,
            tag,
            version.FileNonceBase64,
            version.EncryptedDekBase64,
            version.EncryptionKeyId);

        var ext = version.Extension;
        if (!string.IsNullOrWhiteSpace(ext) && !ext.StartsWith('.'))
            ext = "." + ext;
        var tempPath = Path.Combine(Path.GetTempPath(), $"dms_{Guid.NewGuid():N}{ext}");
        await File.WriteAllBytesAsync(tempPath, plaintext, ct);

        return new DocumentVersionLocalFile { Path = tempPath, DeleteWhenDisposed = true };
    }

    public bool FileExists(DocumentVersion version)
    {
        var fullPath = storage.ResolveFullPath(version.StoredPath);
        return File.Exists(fullPath);
    }

    public string ResolveFullPath(string relativePath) => storage.ResolveFullPath(relativePath);
}

public static class DocumentVersionEncryptionMetadata
{
    public static void Apply(DocumentVersion version, DocumentVersionSaveResult saved)
    {
        version.IsEncrypted = saved.IsEncrypted;
        version.EncryptionKeyId = saved.EncryptionKeyId;
        version.FileNonceBase64 = saved.FileNonceBase64;
        version.EncryptedDekBase64 = saved.EncryptedDekBase64;
        version.SizeBytes = saved.PlaintextSizeBytes;
    }
}

public static class DocumentVersionFileAccessFormExtensions
{
    public static Task<DocumentVersionSaveResult> SaveFromFormFileAsync(
        this IDocumentVersionFileAccess files,
        Guid documentId,
        int versionNumber,
        IFormFile file,
        CancellationToken ct = default)
    {
        return files.SaveFromStreamAsync(documentId, versionNumber, file.OpenReadStream(), file.FileName, ct);
    }
}
