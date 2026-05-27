using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public static class ContractSoftDelete
{
    public sealed record UsageSnapshot(
        bool WorkflowStarted,
        bool WorkflowInProgress,
        bool HasPostApproval,
        int ActiveApprovalLinks,
        int ActiveActionLinks);

    public static UsageSnapshot Analyze(Contract contract)
    {
        var workflowStarted = contract.WorkflowStartedAtUtc.HasValue;
        var inProgress = contract.Status is ContractStatus.Pending
            or ContractStatus.InProgress
            or ContractStatus.Suspended;
        var hasPostApproval = contract.Status == ContractStatus.Approved
            && !string.IsNullOrWhiteSpace(contract.PostApprovalJson);

        return new UsageSnapshot(
            workflowStarted,
            workflowStarted && inProgress,
            hasPostApproval,
            0,
            0);
    }

    public static async Task<UsageSnapshot> AnalyzeAsync(AppDbContext db, Contract contract, CancellationToken ct = default)
    {
        var baseInfo = Analyze(contract);
        var now = DateTime.UtcNow;
        var activeApproval = await db.ContractApprovalLinks
            .CountAsync(x => x.ContractId == contract.Id && x.IsActive && x.ExpiresAtUtc > now, ct);
        var activeAction = await db.ContractActionLinks
            .CountAsync(x => x.ContractId == contract.Id && x.IsActive && x.ExpiresAtUtc > now, ct);

        return baseInfo with
        {
            ActiveApprovalLinks = activeApproval,
            ActiveActionLinks = activeAction,
        };
    }

    public static string BuildMessage(string contractNumber, UsageSnapshot usage)
    {
        var parts = new List<string>();
        if (usage.WorkflowInProgress)
            parts.Add("گردش در جریان");
        else if (usage.WorkflowStarted)
            parts.Add("گردش آغاز شده");
        if (usage.HasPostApproval)
            parts.Add("فاز اقدام");
        if (usage.ActiveApprovalLinks > 0)
            parts.Add($"{usage.ActiveApprovalLinks} لینک تأیید فعال");
        if (usage.ActiveActionLinks > 0)
            parts.Add($"{usage.ActiveActionLinks} لینک اقدام فعال");

        if (parts.Count == 0)
            return $"قرارداد «{contractNumber}» حذف شد (نرم) و از لیست خارج می‌شود.";

        return
            $"قرارداد «{contractNumber}» حذف شد (نرم). با وجود: {string.Join("، ", parts)}. " +
            "دیگر در لیست و لینک‌های عمومی فعال نمایش داده نمی‌شود؛ سوابق در دیتابیس باقی می‌ماند.";
    }

    public static async Task ApplyAsync(
        AppDbContext db,
        Contract contract,
        Guid? deletedByUserId,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        contract.IsSoftDeleted = true;
        contract.IsArchived = true;
        contract.DeletedAtUtc = now;
        contract.DeletedByUserId = deletedByUserId;

        var approvalLinks = await db.ContractApprovalLinks
            .Where(x => x.ContractId == contract.Id && x.IsActive)
            .ToListAsync(ct);
        foreach (var link in approvalLinks)
            link.IsActive = false;

        var actionLinks = await db.ContractActionLinks
            .Where(x => x.ContractId == contract.Id && x.IsActive)
            .ToListAsync(ct);
        foreach (var link in actionLinks)
            link.IsActive = false;
    }
}
