using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Documents;

public sealed class DocumentTextExtractionProcessor(
    AppDbContext db,
    DocumentFileStorageService storage,
    TextExtractorResolver extractorResolver,
    IFarsiTextNormalizer normalizer,
    ILogger<DocumentTextExtractionProcessor> logger) : IDocumentTextExtractionProcessor
{
    private const int MaxAttempts = 3;

    public async Task ProcessVersionAsync(Guid documentVersionId, CancellationToken cancellationToken = default)
    {
        var row = await db.DocumentVersionTexts
            .Include(x => x.Version)
            .FirstOrDefaultAsync(x => x.DocumentVersionId == documentVersionId, cancellationToken);

        if (row is null)
        {
            var version = await db.DocumentVersions.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == documentVersionId, cancellationToken);
            if (version is null) return;

            row = new DocumentVersionText
            {
                DocumentVersionId = version.Id,
                DocumentId = version.DocumentId,
                ProcessingStatus = DocumentTextProcessingStatus.Pending,
            };
            db.DocumentVersionTexts.Add(row);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (row.ProcessingStatus is DocumentTextProcessingStatus.Succeeded or DocumentTextProcessingStatus.Skipped)
            return;

        if (row.AttemptCount >= MaxAttempts)
        {
            row.ProcessingStatus = DocumentTextProcessingStatus.Failed;
            row.ErrorMessage = "حداکثر تلاش برای استخراج متن انجام شد";
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        row.ProcessingStatus = DocumentTextProcessingStatus.Processing;
        row.AttemptCount += 1;
        row.ErrorMessage = null;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            var version = row.Version ?? await db.DocumentVersions
                .AsNoTracking()
                .FirstAsync(x => x.Id == documentVersionId, cancellationToken);

            var ext = version.Extension.Trim().TrimStart('.');
            var extractor = extractorResolver.Resolve(ext);
            if (extractor is null)
            {
                row.ProcessingStatus = DocumentTextProcessingStatus.Skipped;
                row.ErrorMessage = $"استخراج متن برای پسوند .{ext} پشتیبانی نمی‌شود";
                row.ProcessedAtUtc = DateTime.UtcNow;
                row.UpdatedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var fullPath = storage.ResolveFullPath(version.StoredPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException("فایل فیزیکی یافت نشد", fullPath);

            var extracted = await extractor.ExtractAsync(fullPath, cancellationToken);
            var normalized = normalizer.Normalize(extracted);

            row.ExtractedText = extracted;
            row.NormalizedText = normalized;
            row.CharCount = normalized.Length;
            row.ProcessingStatus = string.IsNullOrWhiteSpace(normalized)
                ? DocumentTextProcessingStatus.Skipped
                : DocumentTextProcessingStatus.Succeeded;
            if (row.ProcessingStatus == DocumentTextProcessingStatus.Skipped)
                row.ErrorMessage = "متنی استخراج نشد (احتمالاً PDF اسکن‌شده یا فایل خالی)";
            row.ProcessedAtUtc = DateTime.UtcNow;
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Document text extracted version={VersionId} chars={Chars} status={Status}",
                documentVersionId,
                row.CharCount,
                row.ProcessingStatus);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Document text extraction failed version={VersionId}", documentVersionId);
            row.ProcessingStatus = row.AttemptCount >= MaxAttempts
                ? DocumentTextProcessingStatus.Failed
                : DocumentTextProcessingStatus.Pending;
            row.ErrorMessage = ex.Message.Length > 1900 ? ex.Message[..1900] : ex.Message;
            row.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
