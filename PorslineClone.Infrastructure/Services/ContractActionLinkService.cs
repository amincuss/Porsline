using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class ContractActionLinkService(AppDbContext db)
{
    private const string CodeChars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<string> CreateOrRefreshAsync(Guid contractId, Guid assigneeUserId, DateTime expiresAtUtc, CancellationToken ct = default)
    {
        var existing = await db.ContractActionLinks
            .Where(x => x.ContractId == contractId && x.AssigneeUserId == assigneeUserId && x.IsActive)
            .ToListAsync(ct);
        foreach (var link in existing)
            link.IsActive = false;

        var code = await GenerateUniqueCodeAsync(ct);
        db.ContractActionLinks.Add(new ContractActionLink
        {
            Id = Guid.NewGuid(),
            ContractId = contractId,
            AssigneeUserId = assigneeUserId,
            Code = code,
            IsActive = true,
            ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return code;
    }

    public async Task<ContractActionLink?> ResolveByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var normalized = code.Trim();
        var link = await db.ContractActionLinks
            .IgnoreQueryFilters()
            .Include(x => x.Contract)
            .FirstOrDefaultAsync(x => x.Code == normalized && x.IsActive, ct);
        if (link is null || link.Contract is null || link.Contract.IsSoftDeleted) return null;
        return link;
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        for (var i = 0; i < 12; i++)
        {
            var code = new string(Enumerable.Range(0, 10)
                .Select(_ => CodeChars[Random.Shared.Next(CodeChars.Length)])
                .ToArray());
            if (!await db.ContractActionLinks.AnyAsync(x => x.Code == code, ct)
                && !await db.ContractApprovalLinks.AnyAsync(x => x.Code == code, ct))
                return code;
        }
        return Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
    }
}
