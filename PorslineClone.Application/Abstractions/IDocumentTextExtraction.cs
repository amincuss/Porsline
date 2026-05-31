namespace PorslineClone.Application.Abstractions;

public interface ITextExtractor
{
    /// <summary>پسوندهای پشتیبانی‌شده بدون نقطه، مثلاً pdf, docx</summary>
    IReadOnlyCollection<string> SupportedExtensions { get; }

    bool CanExtract(string extension);

    Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default);
}

public interface IFarsiTextNormalizer
{
    string Normalize(string? input);
}

public interface IDocumentTextExtractionQueue
{
    ValueTask EnqueueAsync(Guid documentVersionId, CancellationToken cancellationToken = default);
}

public interface IDocumentTextExtractionProcessor
{
    Task ProcessVersionAsync(Guid documentVersionId, CancellationToken cancellationToken = default);
}

public sealed record DocumentContentSearchHit(
    Guid DocumentId,
    string Title,
    string? ReferenceNumber,
    int VersionNumber,
    string Extension,
    double Rank,
    string Snippet,
    string ProcessingStatus);

public interface IDocumentContentSearchService
{
    Task<(int Total, IReadOnlyList<DocumentContentSearchHit> Items)> SearchAsync(
        string query,
        int skip,
        int take,
        DateTime? createdStartUtc = null,
        DateTime? createdEndUtc = null,
        CancellationToken cancellationToken = default);
}
