using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class FormSubmissionApprovalLinkService(AppDbContext db)
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<string> CreateOrRefreshAsync(Guid formSubmissionId, Guid assigneeUserId, CancellationToken ct = default)
    {
        var existing = await db.FormSubmissionApprovalLinks
            .Where(x => x.FormSubmissionId == formSubmissionId && x.AssigneeUserId == assigneeUserId && x.IsActive)
            .ToListAsync(ct);
        foreach (var link in existing)
            link.IsActive = false;

        var security = await SecuritySettingsHelper.GetAsync(db, ct);
        var code = await GenerateUniqueCodeAsync(ct);
        db.FormSubmissionApprovalLinks.Add(new FormSubmissionApprovalLink
        {
            Id = Guid.NewGuid(),
            FormSubmissionId = formSubmissionId,
            AssigneeUserId = assigneeUserId,
            Code = code,
            IsActive = true,
            ExpiresAtUtc = SecuritySettingsHelper.LinkExpiresAtUtc(security),
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<FormSubmissionApprovalLink?> ResolveValidAsync(string code, CancellationToken ct = default)
    {
        var link = await ResolveByCodeAsync(code, ct);
        if (link is null || !link.IsActive) return null;
        return link;
    }

    public async Task<FormSubmissionApprovalLink?> ResolveByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = code.Trim();
        var link = await db.FormSubmissionApprovalLinks
            .Include(x => x.FormSubmission)
            .ThenInclude(s => s.Form)
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
            if (!await db.FormSubmissionApprovalLinks.AnyAsync(x => x.Code == code, ct))
                return code;
        }
        return Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }
}
