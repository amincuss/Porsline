using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Documents;

public sealed class DocumentEncryptionKeyRotationService(
    AppDbContext db,
    DocumentEnvelopeEncryptionService encryption,
    ILogger<DocumentEncryptionKeyRotationService> logger) : IDocumentEncryptionKeyRotationService
{
    public async Task<DocumentDekRotationResult> RotateDekWrappersAsync(
        Guid? documentId = null,
        int batchSize = 200,
        CancellationToken ct = default)
    {
        if (!encryption.IsEncryptionActive)
            throw new InvalidOperationException("Document encryption is not enabled or master keys are not configured.");

        var primaryId = encryption.PrimaryKeyId;
        var result = new DocumentDekRotationResult { PrimaryKeyId = primaryId };

        var scanned = 0;
        var rotated = 0;
        var skipped = 0;
        var failed = 0;
        var offset = 0;

        while (true)
        {
            var query = db.DocumentVersions
                .Where(v => v.IsEncrypted && v.EncryptedDekBase64 != null && v.EncryptionKeyId != null);

            if (documentId.HasValue)
                query = query.Where(v => v.DocumentId == documentId.Value);

            var batch = await query
                .OrderBy(v => v.UploadedAtUtc)
                .Skip(offset)
                .Take(batchSize)
                .ToListAsync(ct);

            if (batch.Count == 0)
                break;

            offset += batch.Count;

            foreach (var version in batch)
            {
                scanned++;
                if (string.Equals(version.EncryptionKeyId, primaryId, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    version.EncryptedDekBase64 = encryption.RewrapDek(
                        version.EncryptedDekBase64!,
                        version.EncryptionKeyId!,
                        primaryId);
                    version.EncryptionKeyId = primaryId;
                    rotated++;
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogWarning(
                        ex,
                        "DEK rewrap failed for DocumentVersion {VersionId}",
                        version.Id);
                }
            }

            await db.SaveChangesAsync(ct);
            if (batch.Count < batchSize)
                break;
        }

        return new DocumentDekRotationResult
        {
            Scanned = scanned,
            Rotated = rotated,
            Skipped = skipped,
            Failed = failed,
            PrimaryKeyId = primaryId,
        };
    }
}
