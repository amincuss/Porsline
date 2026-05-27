using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class FormActionLinkService(AppDbContext db)
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<string> CreateOrRefreshAsync(
        Guid formSubmissionId,
        Guid assigneeUserId,
        DateTime expiresAtUtc,
        CancellationToken ct = default)
    {
        var existing = await db.FormActionLinks
            .Where(x => x.FormSubmissionId == formSubmissionId && x.AssigneeUserId == assigneeUserId && x.IsActive)
            .ToListAsync(ct);
        foreach (var link in existing)
            link.IsActive = false;

        var code = await GenerateUniqueCodeAsync(ct);
        db.FormActionLinks.Add(new FormActionLink
        {
            Id = Guid.NewGuid(),
            FormSubmissionId = formSubmissionId,
            AssigneeUserId = assigneeUserId,
            Code = code,
            IsActive = true,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow,
        });
        await db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<FormActionLink?> ResolveByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = code.Trim();
        var link = await db.FormActionLinks
            .Include(x => x.FormSubmission)
            .ThenInclude(s => s.Form)
            .FirstOrDefaultAsync(x => x.Code == normalized && x.IsActive, ct);
        if (link is null || link.FormSubmission is null || link.FormSubmission.Form is null || link.FormSubmission.Form.IsDeleted)
            return null;
        if (link.ExpiresAtUtc < DateTime.UtcNow)
            return null;
        return link;
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (var i = 0; i < 12; i++)
        {
            var code = new string(Enumerable.Range(0, 10)
                .Select(_ => CodeChars[Random.Shared.Next(CodeChars.Length)])
                .ToArray());
            if (!await CodeExistsAsync(code, ct))
                return code;
        }

        return Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }

    private async Task<bool> CodeExistsAsync(string code, CancellationToken ct) =>
        await db.FormActionLinks.AnyAsync(x => x.Code == code, ct)
        || await db.FormSubmissionApprovalLinks.AnyAsync(x => x.Code == code, ct)
        || await db.ContractActionLinks.AnyAsync(x => x.Code == code, ct)
        || await db.ContractApprovalLinks.AnyAsync(x => x.Code == code, ct)
        || await db.FormDispatchLinks.AnyAsync(x => x.Code == code, ct);
}
