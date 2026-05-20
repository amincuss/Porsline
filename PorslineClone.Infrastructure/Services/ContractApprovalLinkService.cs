using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class ContractApprovalLinkService(AppDbContext db)
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<string> CreateOrRefreshAsync(Guid contractId, Guid assigneeUserId, CancellationToken ct = default)
    {
        var existing = await db.ContractApprovalLinks
            .Where(x => x.ContractId == contractId && x.AssigneeUserId == assigneeUserId && x.IsActive)
            .ToListAsync(ct);
        foreach (var link in existing)
            link.IsActive = false;

        var security = await SecuritySettingsHelper.GetAsync(db, ct);
        var code = await GenerateUniqueCodeAsync(ct);
        db.ContractApprovalLinks.Add(new ContractApprovalLink
        {
            Id = Guid.NewGuid(),
            ContractId = contractId,
            AssigneeUserId = assigneeUserId,
            Code = code,
            IsActive = true,
            ExpiresAtUtc = SecuritySettingsHelper.LinkExpiresAtUtc(security),
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<ContractApprovalLink?> ResolveValidAsync(string code, CancellationToken ct = default)
    {
        var link = await ResolveByCodeAsync(code, ct);
        if (link is null || !link.IsActive) return null;
        return link;
    }

    /// <summary>لینک معتبر (فعال یا پس از تأیید) برای مشاهده گردش و فایل — تا انقضا.</summary>
    public async Task<ContractApprovalLink?> ResolveByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = code.Trim();
        var link = await db.ContractApprovalLinks
            .Include(x => x.Contract)
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
            if (!await db.ContractApprovalLinks.AnyAsync(x => x.Code == code, ct))
                return code;
        }
        return Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }
}
