using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.SmsPatterns;

namespace PorslineClone.Infrastructure.Services.Documents;

public class DocumentPostApprovalService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ISmsSender smsSender,
    ISmsPatternService smsPatterns,
    IInboxMessageService inbox,
    IFrontendUrlResolver frontendUrls)
{
    public async Task TryStartPostApprovalAsync(Document document, CancellationToken ct = default)
    {
        if (document.WorkflowStatus != DocumentWorkflowStatus.Approved) return;

        var existing = PostApprovalJsonHelper.DeserializeState(document.PostApprovalJson);
        if (existing is { AssigneeUserIds.Count: > 0 }) return;

        DocumentWorkflowTemplate? template = null;
        if (document.WorkflowTemplateId is not null)
            template = await db.DocumentWorkflowTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == document.WorkflowTemplateId, ct);

        var assigneeIds = template is not null
            ? PostApprovalJsonHelper.ParseUserIds(template.ActionAssigneeUserIdsJson)
            : [];
        if (assigneeIds.Count == 0) return;

        var dirKey = template?.ActionDirectionKey ?? "";
        var dirLabel = !string.IsNullOrWhiteSpace(template?.ActionDirectionLabel)
            ? template!.ActionDirectionLabel!
            : PostApprovalDirections.LabelFor(dirKey) ?? dirKey;

        var state = new ContractPostApprovalStateDto(
            dirKey,
            dirLabel,
            assigneeIds,
            "pending",
            null,
            null,
            null,
            null,
            null);

        document.PostApprovalJson = PostApprovalJsonHelper.SerializeState(state);
        await db.SaveChangesAsync(ct);

        await NotifyActionAssigneesAsync(document, assigneeIds, dirLabel, ct);
    }

    private async Task NotifyActionAssigneesAsync(
        Document document,
        IReadOnlyList<Guid> assigneeIds,
        string directionLabel,
        CancellationToken ct)
    {
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);
        var adminPath = string.IsNullOrWhiteSpace(adminBase)
            ? $"/admin/documents/workflow-runs?id={document.Id}"
            : $"{adminBase.TrimEnd('/')}/admin/documents/workflow-runs?id={document.Id}";

        foreach (var userId in assigneeIds)
        {
            var user = await userManager.FindByIdAsync(userId.ToString());
            var staffName = user is null
                ? "کارشناس"
                : ResponderHonorific.FormatFullName($"{user.FirstName} {user.LastName}".Trim(), user.Gender);

            var msg = await smsPatterns.RenderAsync("document.postapproval.assignee", SmsPatternVars.Dict(
                ("staffName", staffName),
                ("docTitle", document.Title),
                ("directionLabel", directionLabel),
                ("adminPath", adminPath)
            ), ct);

            await inbox.SendToUserAsync(userId, "اقدام پس از تأیید سند", msg, ct);

            var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
            if (smsSettings.DocumentPostApprovalAssigneeSmsEnabled && !string.IsNullOrWhiteSpace(user?.PhoneNumber))
                await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
        }
    }

    public async Task<(bool Ok, string? Error)> UpdateStatusAsync(
        Guid documentId,
        Guid actorUserId,
        string status,
        string? note,
        CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(x => x.Id == documentId, ct);
        if (document is null) return (false, "سند یافت نشد");

        var state = PostApprovalJsonHelper.DeserializeState(document.PostApprovalJson);
        if (state is null || state.AssigneeUserIds.Count == 0)
            return (false, "فاز اقدام برای این سند تعریف نشده است");

        if (!state.AssigneeUserIds.Contains(actorUserId))
            return (false, "شما در لیست اقدام‌کنندگان این سند نیستید");

        var normalized = status.Trim().ToLowerInvariant();
        if (normalized is not ("pending" or "in_progress" or "completed"))
            return (false, "وضعیت نامعتبر است");

        if (normalized == "completed" && string.IsNullOrWhiteSpace(note))
            return (false, "برای اتمام کار ثبت توضیحات الزامی است");

        var actor = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == actorUserId, ct);
        var actorName = actor is null ? "" : $"{actor.FirstName} {actor.LastName}".Trim();
        var now = DateTime.UtcNow;

        state = state with
        {
            Status = normalized,
            Note = normalized == "completed" ? note?.Trim() : note?.Trim() ?? state.Note,
            UpdatedByUserId = actorUserId,
            UpdatedByUserName = actorName,
            UpdatedAtUtc = now,
            CompletedAtUtc = normalized == "completed" ? now : state.CompletedAtUtc,
        };

        document.PostApprovalJson = PostApprovalJsonHelper.SerializeState(state);
        await db.SaveChangesAsync(ct);
        return (true, null);
    }
}
