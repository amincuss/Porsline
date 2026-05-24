using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public static class ContractActionPhaseHelper
{
    public static async Task<ContractActionPhaseViewDto?> BuildViewAsync(
        Contract contract,
        AppDbContext db,
        CancellationToken ct = default)
    {
        var templates = await LoadTemplatesAsync([contract.WorkflowTemplateId], db, ct);
        var source = ResolveSource(contract, templates);
        if (source is null) return null;

        var nameLookup = await ResolveUserNamesAsync(source.Value.AssigneeIds, db, ct);
        return BuildView(contract, templates, nameLookup);
    }

    public static async Task<IReadOnlyDictionary<Guid, ContractWorkflowTemplate>> LoadTemplatesAsync(
        IEnumerable<Guid?> templateIds,
        AppDbContext db,
        CancellationToken ct)
    {
        var ids = templateIds.Where(x => x.HasValue).Select(x => x!.Value).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, ContractWorkflowTemplate>();

        return await db.ContractWorkflowTemplates.AsNoTracking()
            .Where(t => ids.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, ct);
    }

    public static async Task<IReadOnlyDictionary<Guid, string>> ResolveUserNamesForContractsAsync(
        IEnumerable<Contract> contracts,
        IReadOnlyDictionary<Guid, ContractWorkflowTemplate> templates,
        AppDbContext db,
        CancellationToken ct)
    {
        var assigneeIds = new List<Guid>();
        foreach (var contract in contracts)
        {
            var source = ResolveSource(contract, templates);
            if (source is null) continue;
            assigneeIds.AddRange(source.Value.AssigneeIds);
        }

        return await ResolveUserNamesAsync(assigneeIds, db, ct);
    }

    public static ContractActionPhaseViewDto? BuildView(
        Contract contract,
        IReadOnlyDictionary<Guid, ContractWorkflowTemplate> templates,
        IReadOnlyDictionary<Guid, string> userNames)
    {
        var source = ResolveSource(contract, templates);
        if (source is null) return null;

        var (label, assigneeIds, status, updatedByUserName, updatedAtUtc) = source.Value;
        var assignees = assigneeIds
            .Select(id => new ContractActionPhaseAssigneeDto(id, userNames.GetValueOrDefault(id, "")))
            .ToList();

        return new ContractActionPhaseViewDto(
            label,
            status,
            status is null ? null : PostApprovalJsonHelper.StatusLabel(status),
            assignees,
            updatedByUserName,
            updatedAtUtc);
    }

    public static (string Label, List<Guid> AssigneeIds, string? Status, string? UpdatedByUserName, DateTime? UpdatedAtUtc)?
        ResolveSource(Contract contract, IReadOnlyDictionary<Guid, ContractWorkflowTemplate> templates)
    {
        var state = PostApprovalJsonHelper.DeserializeState(contract.PostApprovalJson);
        if (state is not null && state.AssigneeUserIds.Count > 0)
        {
            return (
                state.ActionDirectionLabel,
                state.AssigneeUserIds,
                state.Status,
                state.UpdatedByUserName,
                state.UpdatedAtUtc);
        }

        if (contract.WorkflowTemplateId is null
            || !templates.TryGetValue(contract.WorkflowTemplateId.Value, out var template))
            return null;

        var assigneeIds = PostApprovalJsonHelper.ParseUserIds(template.ActionAssigneeUserIdsJson);
        if (assigneeIds.Count == 0) return null;

        var label = !string.IsNullOrWhiteSpace(template.ActionDirectionLabel)
            ? template.ActionDirectionLabel!
            : PostApprovalDirections.LabelFor(template.ActionDirectionKey) ?? template.ActionDirectionKey ?? "جهت اقدام";

        return (label, assigneeIds, null, null, null);
    }

    private static async Task<IReadOnlyDictionary<Guid, string>> ResolveUserNamesAsync(
        IEnumerable<Guid> assigneeIds,
        AppDbContext db,
        CancellationToken ct)
    {
        var ids = assigneeIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return new Dictionary<Guid, string>();

        return await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
            .ToDictionaryAsync(
                u => u.Id,
                u =>
                {
                    var full = $"{u.FirstName} {u.LastName}".Trim();
                    return string.IsNullOrWhiteSpace(full) ? (u.UserName ?? "") : full;
                },
                ct);
    }
}
