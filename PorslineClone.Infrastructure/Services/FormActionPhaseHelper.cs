using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public static class FormActionPhaseHelper
{
    public static bool HasActiveActionPhase(FormSubmission submission)
    {
        var state = PostApprovalJsonHelper.DeserializeState(submission.PostApprovalJson);
        if (state is null || state.AssigneeUserIds.Count == 0) return false;
        return !string.Equals(state.Status, "completed", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAwaitingUserAction(FormSubmission submission, Guid userGuid)
    {
        if (userGuid == Guid.Empty) return false;
        if (submission.Status != FormSubmissionStatus.Approved) return false;
        if (submission.IsArchived) return false;

        var state = PostApprovalJsonHelper.DeserializeState(submission.PostApprovalJson);
        if (state is null || state.AssigneeUserIds.Count == 0) return false;
        if (!state.AssigneeUserIds.Contains(userGuid)) return false;

        var st = (state.Status ?? "").Trim().ToLowerInvariant();
        return st is "pending" or "in_progress";
    }

    public static async Task<FormActionPhaseViewDto?> BuildViewAsync(
        FormSubmission submission,
        AppDbContext db,
        CancellationToken ct = default)
    {
        var state = PostApprovalJsonHelper.DeserializeState(submission.PostApprovalJson);
        if (state is null || state.AssigneeUserIds.Count == 0)
        {
            if (submission.Status != FormSubmissionStatus.Approved || submission.WorkflowTemplateId is null)
                return null;

            var template = await db.FormWorkflowTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == submission.WorkflowTemplateId, ct);
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
            .Select(u => new
            {
                u.Id,
                Name = (u.FirstName + " " + u.LastName).Trim(),
                u.UserName,
            })
            .ToDictionaryAsync(
                u => u.Id,
                u => string.IsNullOrWhiteSpace(u.Name) ? (u.UserName ?? "") : u.Name,
                ct);
    }
}

public record FormActionPhaseAssigneeDto(Guid UserId, string UserName);

public record FormActionPhaseViewDto(
    string ActionDirectionLabel,
    string? Status,
    string? StatusLabel,
    IReadOnlyList<FormActionPhaseAssigneeDto> Assignees,
    string? UpdatedByUserName,
    DateTime? UpdatedAtUtc,
    string? Note);
