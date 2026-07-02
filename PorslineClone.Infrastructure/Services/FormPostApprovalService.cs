using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.SmsPatterns;

namespace PorslineClone.Infrastructure.Services;

public class FormPostApprovalService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    FormActionLinkService actionLinks,
    ISmsSender smsSender,
    ISmsPatternService smsPatterns,
    IInboxMessageService inbox,
    IFrontendUrlResolver frontendUrls,
    FormDispatchSubmissionNotifier dispatchNotifier)
{
    public async Task TryStartPostApprovalAsync(FormSubmission submission, CancellationToken ct = default)
    {
        if (submission.Status != FormSubmissionStatus.Approved) return;

        var existing = PostApprovalJsonHelper.DeserializeState(submission.PostApprovalJson);
        if (existing is { AssigneeUserIds.Count: > 0 }) return;

        FormWorkflowTemplate? template = null;
        if (submission.WorkflowTemplateId is not null)
            template = await db.FormWorkflowTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == submission.WorkflowTemplateId, ct);

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

        submission.PostApprovalJson = PostApprovalJsonHelper.SerializeState(state);
        await db.SaveChangesAsync(ct);

        await NotifyActionAssigneesAsync(submission, assigneeIds, dirLabel, ct);
    }

    private async Task NotifyActionAssigneesAsync(
        FormSubmission submission,
        IReadOnlyList<Guid> assigneeIds,
        string directionLabel,
        CancellationToken ct)
    {
        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        var smsEnabled = smsSettings.ApprovalReferralSmsEnabled;

        var formTitle = submission.Form?.Title ?? await db.Forms.AsNoTracking()
            .Where(f => f.Id == submission.FormId)
            .Select(f => f.Title)
            .FirstOrDefaultAsync(ct) ?? "فرم";

        var responderName = submission.SubmitterName?.Trim() ?? "";
        if (submission.DispatchLinkId is Guid linkId)
        {
            var link = await db.FormDispatchLinks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (!string.IsNullOrWhiteSpace(link?.ResponderFullName))
                responderName = link.ResponderFullName.Trim();
            var responder = link is not null
                ? await db.Responders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == link.ResponderId, ct)
                : null;
            var honorific = ResponderHonorific.FormatFullName(responderName, responder?.Gender);
            responderName = honorific;
        }

        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var security = await SecuritySettingsHelper.GetAsync(db, ct);
        var linkExpiry = SecuritySettingsHelper.LinkExpiresAtUtc(security);
        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);
        var adminPath = string.IsNullOrWhiteSpace(adminBase)
            ? $"/admin/forms/workflow-runs?id={submission.Id}"
            : $"{adminBase.TrimEnd('/')}/admin/forms/workflow-runs?id={submission.Id}";

        foreach (var userId in assigneeIds)
        {
            var code = await actionLinks.CreateOrRefreshAsync(submission.Id, userId, linkExpiry, ct);
            var actionPath = string.IsNullOrWhiteSpace(publicBase)
                ? $"/action/form?c={code}"
                : $"{publicBase.TrimEnd('/')}/action/form?c={code}";

            var user = await userManager.FindByIdAsync(userId.ToString());
            var staffName = user is null
                ? "کارشناس"
                : ResponderHonorific.FormatFullName($"{user.FirstName} {user.LastName}".Trim(), user.Gender);

            var msg = await smsPatterns.RenderAsync("form.postapproval.assignee", SmsPatternVars.Dict(
                ("staffName", staffName),
                ("formTitle", formTitle),
                ("responderName", responderName),
                ("dateStr", dateStr),
                ("timeStr", timeStr),
                ("directionLabel", directionLabel),
                ("actionPath", actionPath),
                ("adminPath", adminPath)
            ), ct);

            await inbox.SendToUserAsync(userId, "اقدام پس از تأیید فرم", msg, ct);

            if (smsEnabled && !string.IsNullOrWhiteSpace(user?.PhoneNumber))
                await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
        }
    }

    public async Task<(bool Ok, string? Error)> UpdateStatusAsync(
        Guid submissionId,
        Guid actorUserId,
        string status,
        string? note,
        CancellationToken ct = default)
    {
        var submission = await db.FormSubmissions
            .Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == submissionId, ct);
        if (submission is null) return (false, "پاسخ فرم یافت نشد");

        var state = PostApprovalJsonHelper.DeserializeState(submission.PostApprovalJson);
        if (state is null || state.AssigneeUserIds.Count == 0)
            return (false, "فاز اقدام برای این پاسخ تعریف نشده است");

        if (!state.AssigneeUserIds.Contains(actorUserId))
            return (false, "شما در لیست اقدام‌کنندگان این فرم نیستید");

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

        if (normalized == "completed")
            submission.IsArchived = true;

        submission.PostApprovalJson = PostApprovalJsonHelper.SerializeState(state);
        await db.SaveChangesAsync(ct);

        if (normalized == "completed")
            await dispatchNotifier.NotifyAfterActionPhaseCompletedAsync(submission, state, actorName, ct);

        return (true, null);
    }
}
