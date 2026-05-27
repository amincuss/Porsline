using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

/// <summary>فیلترهای SQL سبک برای پرونده‌های فاز اقدام (قبل از Deserialize JSON).</summary>
public static class PostApprovalQueryFilters
{
    public const int ListMaxRows = 200;

    public static IQueryable<Contract> ApprovedWithPostApproval(
        IQueryable<Contract> query,
        bool archived) =>
        query
            .Where(c => c.Status == ContractStatus.Approved)
            .Where(c => c.PostApprovalJson != null && c.PostApprovalJson != "")
            .Where(c => archived ? c.IsArchived : !c.IsArchived);

    public static IQueryable<Contract> ForAssignee(
        IQueryable<Contract> query,
        Guid userId,
        IReadOnlyCollection<Guid> linkedContractIds)
    {
        var uid = userId.ToString();
        return linkedContractIds.Count > 0
            ? query.Where(c => linkedContractIds.Contains(c.Id) || c.PostApprovalJson!.Contains(uid))
            : query.Where(c => c.PostApprovalJson!.Contains(uid));
    }

    public static IQueryable<Contract> PreFilterStatus(
        IQueryable<Contract> query,
        bool activeView,
        string? status)
    {
        if (activeView)
        {
            return query.Where(c =>
                !c.PostApprovalJson!.Contains("\"status\":\"completed\"")
                && !c.PostApprovalJson!.Contains("\"Status\":\"completed\""));
        }

        if (string.IsNullOrWhiteSpace(status))
            return query;

        var s = status.Trim();
        return query.Where(c =>
            c.PostApprovalJson!.Contains($"\"status\":\"{s}\"")
            || c.PostApprovalJson!.Contains($"\"Status\":\"{s}\""));
    }

    public static IQueryable<Contract> PreFilterSearch(IQueryable<Contract> query, string? q)
    {
        if (string.IsNullOrWhiteSpace(q))
            return query;

        var term = q.Trim();
        return query.Where(c =>
            c.ContractNumber.Contains(term)
            || c.Title.Contains(term)
            || c.SubjectPersonName.Contains(term)
            || c.PostApprovalJson!.Contains(term));
    }
}
