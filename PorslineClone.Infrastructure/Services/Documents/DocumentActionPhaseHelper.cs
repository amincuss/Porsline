using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Documents;

public static class DocumentActionPhaseHelper
{
    public static bool HasActiveActionPhase(Document document)
    {
        var state = PostApprovalJsonHelper.DeserializeState(document.PostApprovalJson);
        if (state is null || state.AssigneeUserIds.Count == 0) return false;
        return !string.Equals(state.Status, "completed", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAwaitingUserAction(Document document, Guid userGuid)
    {
        if (userGuid == Guid.Empty) return false;
        if (document.WorkflowStatus != DocumentWorkflowStatus.Approved) return false;

        var state = PostApprovalJsonHelper.DeserializeState(document.PostApprovalJson);
        if (state is null || state.AssigneeUserIds.Count == 0) return false;
        if (!state.AssigneeUserIds.Contains(userGuid)) return false;

        var st = (state.Status ?? "").Trim().ToLowerInvariant();
        return st is "pending" or "in_progress";
    }

    public static async Task<FormActionPhaseViewDto?> BuildViewAsync(
        Document document,
        AppDbContext db,
        CancellationToken ct = default)
    {
        var state = PostApprovalJsonHelper.DeserializeState(document.PostApprovalJson);
        if (state is null || state.AssigneeUserIds.Count == 0)
        {
            if (document.WorkflowStatus != DocumentWorkflowStatus.Approved || document.WorkflowTemplateId is null)
                return null;

            var template = await db.DocumentWorkflowTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == document.WorkflowTemplateId, ct);
            var ids = template is not null
                ? PostApprovalJsonHelper.ParseUserIds(template.ActionAssigneeUserIdsJson)
                : [];
            if (ids.Count == 0) return null;

            var dirLabel = !string.IsNullOrWhiteSpace(template?.ActionDirectionLabel)
                ? template!.ActionDirectionLabel!
                : PostApprovalDirections.LabelFor(template?.ActionDirectionKey) ?? "جهت اقدام";
            var names = await ResolveUserNamesAsync(ids, db, ct);
            return new FormActionPhaseViewDto(
                dirLabel,
                "pending",
                PostApprovalJsonHelper.StatusLabel("pending"),
                ids.Select(id => new FormActionPhaseAssigneeDto(id, names.GetValueOrDefault(id, ""))).ToList(),
                null,
                null,
                null);
        }

        var userNames = await ResolveUserNamesAsync(state.AssigneeUserIds, db, ct);
        return new FormActionPhaseViewDto(
            state.ActionDirectionLabel,
            state.Status,
            PostApprovalJsonHelper.StatusLabel(state.Status),
            state.AssigneeUserIds
                .Select(id => new FormActionPhaseAssigneeDto(id, userNames.GetValueOrDefault(id, "")))
                .ToList(),
            state.UpdatedByUserName,
            state.UpdatedAtUtc,
            state.Note);
    }

    private static async Task<Dictionary<Guid, string>> ResolveUserNamesAsync(
        IEnumerable<Guid> ids,
        AppDbContext db,
        CancellationToken ct)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0) return new Dictionary<Guid, string>();
        return await db.Users.AsNoTracking()
            .Where(u => idList.Contains(u.Id))
            .Select(u => new { u.Id, Name = (u.FirstName + " " + u.LastName).Trim() })
            .ToDictionaryAsync(x => x.Id, x => string.IsNullOrWhiteSpace(x.Name) ? "کاربر" : x.Name, ct);
    }
}
