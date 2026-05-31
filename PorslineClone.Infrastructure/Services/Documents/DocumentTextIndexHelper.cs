using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Documents;

public static class DocumentTextIndexHelper
{
    public static async Task<string> ComputeSha256HexAsync(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static void AddPendingVersionText(AppDbContext db, Guid documentId, Guid documentVersionId)
    {
        db.DocumentVersionTexts.Add(new DocumentVersionText
        {
            DocumentVersionId = documentVersionId,
            DocumentId = documentId,
            ProcessingStatus = DocumentTextProcessingStatus.Pending,
            UpdatedAtUtc = DateTime.UtcNow,
        });
    }

    public static async Task EnqueueAfterSaveAsync(
        IDocumentTextExtractionQueue queue,
        IEnumerable<Guid> documentVersionIds,
        CancellationToken ct)
    {
        foreach (var versionId in documentVersionIds)
            await queue.EnqueueAsync(versionId, ct);
    }
}
