using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.SmsPatterns;

namespace PorslineClone.Infrastructure.Services;

public class FormWorkflowProcessor(
    AppDbContext db,
    UserManager<AppUser> userManager,
    FormSubmissionApprovalLinkService approvalLinks,
    ISmsSender smsSender,
    ISmsPatternService smsPatterns,
    IInboxMessageService inbox,
    IFrontendUrlResolver frontendUrls,
    FormDispatchSubmissionNotifier dispatchNotifier,
    FormPostApprovalService postApproval)
{
    public async Task<WorkflowActionResult> ProcessActionAsync(
        Guid submissionId,
        Guid assigneeUserId,
        bool approve,
        string? comment,
        CancellationToken ct = default)
    {
        var submission = await db.FormSubmissions
            .Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == submissionId, ct);
        if (submission is null) return WorkflowActionResult.Fail("پاسخ فرم یافت نشد", 404);
        if (submission.WorkflowStartedAtUtc is null)
            return WorkflowActionResult.Fail("گردش این پاسخ هنوز شروع نشده است");

        var steps = DeserializeSteps(submission.StepsJson);
        if (steps.Count == 0)
            return WorkflowActionResult.Fail("مرحله‌ای برای گردش وجود ندارد");

        var current = WorkflowStepJsonHelper.FindCurrentPending(steps, submission.CurrentStepOrder);
        if (current is null)
            return WorkflowActionResult.Fail("مرحله فعالی برای این پاسخ وجود ندارد");
        if (current.UserId != assigneeUserId)
            return WorkflowActionResult.Fail("این مرحله به شما ارجاع نشده است", 403);

        submission.CurrentStepOrder = current.Order;

        var becameFullyApproved = false;
        var terminalReject = false;
        var currentUser = await userManager.FindByIdAsync(assigneeUserId.ToString());
        var approverName = currentUser is null
            ? current.UserName
            : $"{currentUser.FirstName} {currentUser.LastName}".Trim();

        if (approve)
        {
            var sigErr = FormApprovalSignatureHelper.ValidateApproverSignature(currentUser);
            if (sigErr is not null)
                return WorkflowActionResult.Fail(sigErr);

            current.Status = "approved";
            current.Comment = comment;
            current.ActionAt = DateTime.UtcNow;
            string? positionTitle = null;
            if (currentUser?.UserPositionId is Guid positionId)
            {
                positionTitle = await db.UserPositions.AsNoTracking()
                    .Where(p => p.Id == positionId)
                    .Select(p => p.Name)
                    .FirstOrDefaultAsync(ct);
            }
            FormApprovalSignatureHelper.CaptureSignatureOnApprove(current, currentUser, positionTitle);

            var next = WorkflowStepJsonHelper.FindNextStep(steps, current);
            if (next is null)
            {
                submission.Status = FormSubmissionStatus.Approved;
                becameFullyApproved = true;
            }
            else
            {
                WorkflowStepJsonHelper.SetSinglePending(steps, next);
                submission.CurrentStepOrder = next.Order;
                submission.Status = FormSubmissionStatus.InProgress;
                await SendAssigneeSmsAsync(submission, next.UserId, approverName, current.UserName, ct);
            }
        }
        else
        {
            current.Status = "rejected";
            current.Comment = comment;
            current.ActionAt = DateTime.UtcNow;

            if (current.OnReject == "continue")
            {
                var next = WorkflowStepJsonHelper.FindNextStep(steps, current);
                if (next is null)
                {
                    submission.Status = FormSubmissionStatus.Rejected;
                    terminalReject = true;
                }
                else
                {
                    WorkflowStepJsonHelper.SetSinglePending(steps, next);
                    submission.CurrentStepOrder = next.Order;
                    submission.Status = FormSubmissionStatus.InProgress;
                    await SendAssigneeSmsAsync(submission, next.UserId, approverName, current.UserName, ct);
                }
            }
            else
            {
                foreach (var later in steps.Where(s => s.Order > current.Order && s.Status == "waiting"))
                    later.Status = "skipped";
                submission.Status = FormSubmissionStatus.Rejected;
                terminalReject = true;
            }

            if (terminalReject)
            {
                submission.IsArchived = false;
                submission.WorkflowRejectionJson = FormWorkflowRejectionHelper.Serialize(new FormWorkflowRejectionStateDto
                {
                    Phase = "awaiting_sender",
                    RejectedAtStepOrder = current.Order,
                    RejectedByUserId = assigneeUserId,
                    RejectedByUserName = string.IsNullOrWhiteSpace(approverName) ? current.UserName : approverName,
                    RejectionComment = comment,
                    RejectedAtUtc = current.ActionAt ?? DateTime.UtcNow,
                });
            }
        }

        submission.StepsJson = WorkflowStepJsonHelper.Serialize(steps);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(comment))
            await NotifyDispatchSenderAboutApproverNoteAsync(submission, assigneeUserId, approverName, comment, approve, ct);

        if (becameFullyApproved)
        {
            await postApproval.TryStartPostApprovalAsync(submission, ct);
            await dispatchNotifier.NotifySenderAfterFullApprovalAsync(submission, ct);
        }
        else if (terminalReject)
        {
            await dispatchNotifier.NotifyAfterRejectAwaitingSenderActionAsync(
                submission, current, approverName, comment, ct);
        }

        return WorkflowActionResult.Ok(approve ? "تأیید شد" : "رد شد");
    }

    public async Task<WorkflowActionResult> ResendPendingApprovalSmsAsync(Guid submissionId, CancellationToken ct = default)
    {
        var submission = await db.FormSubmissions.Include(x => x.Form).FirstOrDefaultAsync(x => x.Id == submissionId, ct);
        if (submission is null) return WorkflowActionResult.Fail("پاسخ فرم یافت نشد", 404);
        if (submission.WorkflowStartedAtUtc is null)
            return WorkflowActionResult.Fail("گردش این پاسخ هنوز شروع نشده است");
        if (submission.Status != FormSubmissionStatus.InProgress)
            return WorkflowActionResult.Fail("گردش در حال اجرا نیست یا به پایان رسیده است");

        var steps = DeserializeSteps(submission.StepsJson);
        var current = WorkflowStepJsonHelper.FindCurrentPending(steps, submission.CurrentStepOrder);
        if (current is null)
            return WorkflowActionResult.Fail("در حال حاضر مرحله‌ای برای تأیید فعال نیست");

        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ApprovalReferralSmsEnabled)
            return WorkflowActionResult.Fail("پیامک ارجاع تأیید در تنظیمات سیستم غیرفعال است");

        var user = await userManager.FindByIdAsync(current.UserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return WorkflowActionResult.Fail("شماره موبایل تأییدکننده فعلی ثبت نشده است");

        var sent = await SendAssigneeSmsAsync(submission, current.UserId, null, "پنل مدیریت", ct, isReminder: true);
        if (!sent)
            return WorkflowActionResult.Fail("ارسال پیامک با خطا مواجه شد. تنظیمات درگاه پیامک را بررسی کنید");

        var name = $"{user.FirstName} {user.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name)) name = user.UserName ?? "تأییدکننده";
        return WorkflowActionResult.Ok($"پیامک یادآوری به {name} ({user.PhoneNumber}) ارسال شد");
    }

    public async Task<(bool Ok, string? Error)> TryStartWorkflowAsync(FormSubmission submission, CancellationToken ct = default)
    {
        if (submission.WorkflowStartedAtUtc is not null)
            return (false, "گردش قبلاً شروع شده است");
        if (submission.Status != FormSubmissionStatus.Pending)
            return (false, "گردش در وضعیت انتظار نیست");

        var steps = DeserializeSteps(submission.StepsJson);
        if (steps.Count == 0)
            return (false, "مرحله‌ای برای گردش وجود ندارد");

        var first = steps.OrderBy(s => s.Order).First();
        WorkflowStepJsonHelper.SetSinglePending(steps, first);
        submission.CurrentStepOrder = first.Order;
        submission.Status = FormSubmissionStatus.InProgress;
        submission.WorkflowStartedAtUtc = DateTime.UtcNow;
        submission.WorkflowScheduledStartAtUtc = null;
        if (submission.WorkflowRunCycle < 1)
            submission.WorkflowRunCycle = 1;
        submission.StepsJson = WorkflowStepJsonHelper.Serialize(steps);
        await db.SaveChangesAsync(ct);

        await SendAssigneeSmsAsync(submission, first.UserId, null, null, ct);
        await dispatchNotifier.NotifyResponderWorkflowStartedAsync(submission, ct);
        return (true, null);
    }

    private async Task<bool> SendAssigneeSmsAsync(
        FormSubmission submission,
        Guid userId,
        string? approverDisplayName,
        string? fallbackApproverName,
        CancellationToken ct,
        bool isReminder = false)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        var sender = !string.IsNullOrWhiteSpace(approverDisplayName) ? approverDisplayName : fallbackApproverName;
        if (string.IsNullOrWhiteSpace(sender)) sender = "سیستم";

        var code = await approvalLinks.CreateOrRefreshAsync(submission.Id, userId, ct);
        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);
        var linkPath = string.IsNullOrWhiteSpace(publicBase)
            ? $"/approve/form?c={code}"
            : $"{publicBase.TrimEnd('/')}/approve/form?c={code}";
        var adminWorkflowRuns = string.IsNullOrWhiteSpace(adminBase)
            ? "/admin/forms/workflow-runs"
            : $"{adminBase.TrimEnd('/')}/admin/forms/workflow-runs";

        var formTitle = submission.Form?.Title ?? "فرم";
        var msg = isReminder
            ? await smsPatterns.RenderAsync("form.approval.assignee.reminder", SmsPatternVars.Dict(
                ("formTitle", formTitle),
                ("linkPath", linkPath),
                ("adminWorkflowRuns", adminWorkflowRuns)
            ), ct)
            : await smsPatterns.RenderAsync("form.approval.assignee.new", SmsPatternVars.Dict(
                ("formTitle", formTitle),
                ("sender", sender),
                ("linkPath", linkPath),
                ("adminWorkflowRuns", adminWorkflowRuns)
            ), ct);

        var inboxTitle = isReminder ? "یادآوری تأیید فرم" : "فرم برای تأیید";
        await inbox.SendToUserAsync(userId, inboxTitle, msg, ct);
        if (!smsSettings.ApprovalReferralSmsEnabled || string.IsNullOrWhiteSpace(user.PhoneNumber)) return false;
        var sent = await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
        if (sent && isReminder)
            await ApprovalReminderService.MarkReminderSentForFormAsync(db, submission.Id, userId, ct);
        return sent;
    }

    private async Task NotifyDispatchSenderAboutApproverNoteAsync(
        FormSubmission submission,
        Guid assigneeUserId,
        string? approverName,
        string comment,
        bool approved,
        CancellationToken ct)
    {
        if (submission.DispatchLinkId is not Guid linkId) return;
        var senderId = await db.FormDispatchLinks.AsNoTracking()
            .Where(x => x.Id == linkId)
            .Select(x => x.SentByUserId)
            .FirstOrDefaultAsync(ct);
        if (senderId is null || senderId == assigneeUserId) return;

        var formTitle = submission.Form?.Title ?? "فرم";
        var status = approved ? "تأیید" : "رد";
        var who = string.IsNullOrWhiteSpace(approverName) ? "تأییدکننده" : approverName;
        await inbox.SendToUserAsync(
            senderId.Value,
            $"یادداشت تأییدکننده — {formTitle}",
            $"پس از {status}، {who} نوشت:\n{comment.Trim()}",
            ct);
    }

    public static List<ApprovalStepDto> DeserializeSteps(string? json) =>
        WorkflowStepJsonHelper.Deserialize(json);
}
