using PorslineClone.Domain.Entities;

namespace PorslineClone.Application.Abstractions;

public sealed class DocumentVersionSaveResult
{
    public required string RelativePath { get; init; }
    public required string OriginalFileName { get; init; }
    public required string Extension { get; init; }
    public long PlaintextSizeBytes { get; init; }
    public bool IsEncrypted { get; init; }
    public string? EncryptionKeyId { get; init; }
    public string? FileNonceBase64 { get; init; }
    public string? EncryptedDekBase64 { get; init; }
}

/// <summary>مسیر محلی قابل خواندن (فایل اصلی یا فایل موقت رمزگشایی‌شده).</summary>
public sealed class DocumentVersionLocalFile : IAsyncDisposable
{
    public required string Path { get; init; }
    public bool DeleteWhenDisposed { get; init; }

    public ValueTask DisposeAsync()
    {
        if (DeleteWhenDisposed)
        {
            try
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
            catch
            {
                // ignore cleanup errors
            }
        }

        return ValueTask.CompletedTask;
    }
}

public interface IDocumentVersionFileAccess
{
    Task<DocumentVersionSaveResult> SaveFromStreamAsync(
        Guid documentId,
        int versionNumber,
        Stream plaintext,
        string originalFileName,
        CancellationToken ct = default);

    Task<DocumentVersionLocalFile> OpenLocalPathAsync(DocumentVersion version, CancellationToken ct = default);

    bool FileExists(DocumentVersion version);

    string ResolveFullPath(string relativePath);
}

public interface IDocumentEncryptionKeyRotationService
{
    /// <summary>DEKها را با KEK فعال (Primary) دوباره می‌پیچد — بدون بازنویسی فایل روی دیسک.</summary>
    Task<DocumentDekRotationResult> RotateDekWrappersAsync(
        Guid? documentId = null,
        int batchSize = 200,
        CancellationToken ct = default);
}

public sealed class DocumentDekRotationResult
{
    public int Scanned { get; init; }
    public int Rotated { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public string PrimaryKeyId { get; init; } = "";
}
