using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class FormPostApprovalService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ISmsSender smsSender,
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

        var formTitle = submission.Form?.Title ?? "فرم";
        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);
        var adminPath = string.IsNullOrWhiteSpace(adminBase)
            ? $"/admin/forms/workflow-runs?id={submission.Id}"
            : $"{adminBase.TrimEnd('/')}/admin/forms/workflow-runs?id={submission.Id}";

        foreach (var userId in assigneeIds)
        {
            var msg =
                $"پاسخ فرم «{formTitle}» پس از تأیید نهایی، جهت اقدام ({dirLabel}) برای شما ارسال شد.\n" +
                $"مشاهده و ثبت وضعیت:\n{adminPath}";

            await inbox.SendToUserAsync(userId, "اقدام پس از تأیید فرم", msg, ct);

            var user = await userManager.FindByIdAsync(userId.ToString());
            var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
            if (smsSettings.ApprovalReferralSmsEnabled && !string.IsNullOrWhiteSpace(user?.PhoneNumber))
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
