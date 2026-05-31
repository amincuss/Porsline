using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Infrastructure.Services.Documents;

public class DocumentApprovalLinkService(AppDbContext db)
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<string> CreateOrRefreshAsync(Guid documentId, Guid assigneeUserId, CancellationToken ct = default)
    {
        var existing = await db.DocumentApprovalLinks
            .Where(x => x.DocumentId == documentId && x.AssigneeUserId == assigneeUserId && x.IsActive)
            .ToListAsync(ct);
        foreach (var link in existing)
            link.IsActive = false;

        var security = await SecuritySettingsHelper.GetAsync(db, ct);
        var code = await GenerateUniqueCodeAsync(ct);
        db.DocumentApprovalLinks.Add(new DocumentApprovalLink
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            AssigneeUserId = assigneeUserId,
            Code = code,
            IsActive = true,
            ExpiresAtUtc = SecuritySettingsHelper.LinkExpiresAtUtc(security),
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<DocumentApprovalLink?> ResolveValidAsync(string code, CancellationToken ct = default)
    {
        var link = await ResolveByCodeAsync(code, ct);
        if (link is null || !link.IsActive) return null;
        return link;
    }

    public async Task<DocumentApprovalLink?> ResolveByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = code.Trim();
        var link = await db.DocumentApprovalLinks
            .Include(x => x.Document)
            .FirstOrDefaultAsync(x => x.Code == normalized, ct);
        if (link is null || link.ExpiresAtUtc < DateTime.UtcNow) return null;
        return link;
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (var i = 0; i < 12; i++)
        {
            var code = new string(Enumerable.Range(0, 10)
                .Select(_ => CodeChars[Random.Shared.Next(CodeChars.Length)])
                .ToArray());
            if (!await db.DocumentApprovalLinks.AnyAsync(x => x.Code == code, ct))
                return code;
        }
        return Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }
}
