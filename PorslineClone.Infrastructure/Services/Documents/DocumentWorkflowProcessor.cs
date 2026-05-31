using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Infrastructure.Services.Documents;

public class DocumentWorkflowProcessor(
    AppDbContext db,
    UserManager<AppUser> userManager,
    DocumentApprovalLinkService approvalLinks,
    ISmsSender smsSender,
    IInboxMessageService inbox,
    IFrontendUrlResolver frontendUrls,
    DocumentPostApprovalService postApproval)
{
    public async Task<WorkflowActionResult> ProcessActionAsync(
        Guid documentId,
        Guid assigneeUserId,
        bool approve,
        string? comment,
        CancellationToken ct = default)
    {
        var document = await db.Documents
            .FirstOrDefaultAsync(x => x.Id == documentId, ct);
        if (document is null) return WorkflowActionResult.Fail("سند یافت نشد", 404);
        if (document.WorkflowStartedAtUtc is null)
            return WorkflowActionResult.Fail("گردش این سند هنوز شروع نشده است");

        var steps = DeserializeSteps(document.StepsJson);
        if (steps.Count == 0)
            return WorkflowActionResult.Fail("مرحله‌ای برای گردش وجود ندارد");

        var current = WorkflowStepJsonHelper.FindCurrentPending(steps, document.CurrentStepOrder);
        if (current is null)
            return WorkflowActionResult.Fail("مرحله فعالی برای این سند وجود ندارد");
        if (current.UserId != assigneeUserId)
            return WorkflowActionResult.Fail("این مرحله به شما ارجاع نشده است", 403);

        document.CurrentStepOrder = current.Order;

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
                document.WorkflowStatus = DocumentWorkflowStatus.Approved;
                becameFullyApproved = true;
            }
            else
            {
                WorkflowStepJsonHelper.SetSinglePending(steps, next);
                document.CurrentStepOrder = next.Order;
                document.WorkflowStatus = DocumentWorkflowStatus.InProgress;
                await SendAssigneeSmsAsync(document, next.UserId, approverName, current.UserName, ct);
                await NotifyOwnerAboutStepApprovalAsync(document, currentUser, next, ct);
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
                    document.WorkflowStatus = DocumentWorkflowStatus.Rejected;
                    terminalReject = true;
                }
                else
                {
                    WorkflowStepJsonHelper.SetSinglePending(steps, next);
                    document.CurrentStepOrder = next.Order;
                    document.WorkflowStatus = DocumentWorkflowStatus.InProgress;
                    await SendAssigneeSmsAsync(document, next.UserId, approverName, current.UserName, ct);
                }
            }
            else
            {
                foreach (var later in steps.Where(s => s.Order > current.Order && s.Status == "waiting"))
                    later.Status = "skipped";
                document.WorkflowStatus = DocumentWorkflowStatus.Rejected;
                terminalReject = true;
            }

            if (terminalReject)
            {
                document.WorkflowRejectionJson = DocumentWorkflowRejectionHelper.Serialize(new FormWorkflowRejectionStateDto
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

        document.StepsJson = WorkflowStepJsonHelper.Serialize(steps);
        await db.SaveChangesAsync(ct);

        if (becameFullyApproved)
        {
            await NotifyOwnerFullyApprovedAsync(document, ct);
            await postApproval.TryStartPostApprovalAsync(document, ct);
        }
        else if (terminalReject)
        {
            await NotifyOwnerRejectedAsync(document, approverName, comment, ct);
        }

        return WorkflowActionResult.Ok(approve ? "تأیید شد" : "رد شد");
    }

    public async Task<WorkflowActionResult> ResendPendingApprovalSmsAsync(Guid documentId, CancellationToken ct = default)
    {
        var document = await db.Documents.FirstOrDefaultAsync(x => x.Id == documentId, ct);
        if (document is null) return WorkflowActionResult.Fail("سند یافت نشد", 404);
        if (document.WorkflowStartedAtUtc is null)
            return WorkflowActionResult.Fail("گردش این سند هنوز شروع نشده است");
        if (document.WorkflowStatus != DocumentWorkflowStatus.InProgress)
            return WorkflowActionResult.Fail("گردش در حال اجرا نیست یا به پایان رسیده است");

        var steps = DeserializeSteps(document.StepsJson);
        var current = WorkflowStepJsonHelper.FindCurrentPending(steps, document.CurrentStepOrder);
        if (current is null)
            return WorkflowActionResult.Fail("در حال حاضر مرحله‌ای برای تأیید فعال نیست");

        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.DocumentApprovalReferralSmsEnabled)
            return WorkflowActionResult.Fail("پیامک ارجاع تأیید سند در تنظیمات غیرفعال است");

        var user = await userManager.FindByIdAsync(current.UserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return WorkflowActionResult.Fail("شماره موبایل تأییدکننده فعلی ثبت نشده است");

        var sent = await SendAssigneeSmsAsync(document, current.UserId, null, "پنل مدیریت", ct, isReminder: true);
        if (!sent)
            return WorkflowActionResult.Fail("ارسال پیامک با خطا مواجه شد. تنظیمات درگاه پیامک را بررسی کنید");

        var name = $"{user.FirstName} {user.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name)) name = user.UserName ?? "تأییدکننده";
        return WorkflowActionResult.Ok($"پیامک یادآوری به {name} ({user.PhoneNumber}) ارسال شد");
    }

    public async Task<(bool Ok, string? Error)> TryStartWorkflowAsync(Document document, CancellationToken ct = default)
    {
        if (document.WorkflowStartedAtUtc is not null)
            return (false, "گردش قبلاً شروع شده است");
        if (document.WorkflowStatus != DocumentWorkflowStatus.Pending)
            return (false, "گردش در وضعیت انتظار نیست");

        var steps = DeserializeSteps(document.StepsJson);
        if (steps.Count == 0)
            return (false, "مرحله‌ای برای گردش وجود ندارد");

        var first = steps.OrderBy(s => s.Order).First();
        WorkflowStepJsonHelper.SetSinglePending(steps, first);
        document.CurrentStepOrder = first.Order;
        document.WorkflowStatus = DocumentWorkflowStatus.InProgress;
        document.WorkflowStartedAtUtc = DateTime.UtcNow;
        document.WorkflowScheduledStartAtUtc = null;
        if (document.WorkflowRunCycle < 1)
            document.WorkflowRunCycle = 1;
        document.StepsJson = WorkflowStepJsonHelper.Serialize(steps);
        await db.SaveChangesAsync(ct);

        await SendAssigneeSmsAsync(document, first.UserId, null, null, ct);
        return (true, null);
    }

    private async Task<bool> SendAssigneeSmsAsync(
        Document document,
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

        var code = await approvalLinks.CreateOrRefreshAsync(document.Id, userId, ct);
        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);
        var linkPath = string.IsNullOrWhiteSpace(publicBase)
            ? $"/approve/document?c={code}"
            : $"{publicBase.TrimEnd('/')}/approve/document?c={code}";
        var adminWorkflowRuns = string.IsNullOrWhiteSpace(adminBase)
            ? "/admin/documents/workflow-runs"
            : $"{adminBase.TrimEnd('/')}/admin/documents/workflow-runs";

        var docTitle = document.Title;
        var msg = isReminder
            ? $"یادآوری: سند «{docTitle}» همچنان منتظر تأیید شماست.\n" +
              $"لینک تأیید (بدون نیاز به ورود):\n{linkPath}\n" +
              $"یا پنل: {adminWorkflowRuns}"
            : $"سند «{docTitle}» برای تأیید شما ارسال شد.\n" +
              $"ارجاع‌دهنده: {sender}\n" +
              $"لینک تأیید (بدون نیاز به ورود):\n{linkPath}\n" +
              $"یا پنل: {adminWorkflowRuns}";

        var inboxTitle = isReminder ? "یادآوری تأیید سند" : "سند برای تأیید";
        await inbox.SendToUserAsync(userId, inboxTitle, msg, ct);
        if (!smsSettings.DocumentApprovalReferralSmsEnabled || string.IsNullOrWhiteSpace(user.PhoneNumber)) return false;
        var sent = await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
        if (sent && isReminder)
            await MarkReminderSentForDocumentAsync(document.Id, userId, ct);
        return sent;
    }

    private async Task MarkReminderSentForDocumentAsync(Guid documentId, Guid userId, CancellationToken ct)
    {
        var link = await db.DocumentApprovalLinks
            .Where(x => x.DocumentId == documentId && x.AssigneeUserId == userId && x.IsActive)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);
        if (link is null) return;
        link.ReminderSmsSentAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task NotifyOwnerAboutStepApprovalAsync(
        Document document,
        AppUser? approver,
        ApprovalStepDto nextStep,
        CancellationToken ct)
    {
        if (document.OwnerUserId == Guid.Empty) return;

        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        var owner = await userManager.FindByIdAsync(document.OwnerUserId.ToString());
        if (owner is null) return;

        var approverLabel = FormatPersonLabel(approver, null);
        var nextUser = await db.Users.AsNoTracking()
            .Include(u => u.UserPosition)
            .FirstOrDefaultAsync(u => u.Id == nextStep.UserId, ct);
        var nextLabel = FormatPersonLabel(nextUser, nextStep.UserName);
        var position = nextUser?.UserPosition?.Name?.Trim();
        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var statusTail = string.IsNullOrWhiteSpace(position)
            ? $"سیستم منتظر تأیید {nextLabel} است."
            : $"سیستم منتظر تأیید {nextLabel} با سمت «{position}» است.";

        var refPart = string.IsNullOrWhiteSpace(document.ReferenceNumber)
            ? ""
            : $" (شماره ارجاع: {document.ReferenceNumber})";
        var msg =
            $"سند «{document.Title}»{refPart}:\n" +
            $"{approverLabel} در تاریخ {dateStr} ساعت {timeStr} تأیید کرد.\n" +
            statusTail;

        await inbox.SendToUserAsync(document.OwnerUserId, "به‌روزرسانی گردش سند", msg, ct);
        if (!smsSettings.DocumentOwnerStepApprovalNotifySmsEnabled || string.IsNullOrWhiteSpace(owner.PhoneNumber))
            return;
        await smsSender.SendSmsAsync(new SmsRequest(owner.PhoneNumber, msg), ct);
    }

    private async Task NotifyOwnerFullyApprovedAsync(Document document, CancellationToken ct)
    {
        if (document.OwnerUserId == Guid.Empty) return;

        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        var owner = await userManager.FindByIdAsync(document.OwnerUserId.ToString());
        if (owner is null) return;

        var refPart = string.IsNullOrWhiteSpace(document.ReferenceNumber)
            ? ""
            : $" (شماره ارجاع: {document.ReferenceNumber})";
        var msg = $"سند «{document.Title}»{refPart} در تمام مراحل تأیید شد و گردش به پایان رسید.";

        await inbox.SendToUserAsync(document.OwnerUserId, "تأیید نهایی سند", msg, ct);
        if (!smsSettings.DocumentWorkflowCompletedOwnerSmsEnabled || string.IsNullOrWhiteSpace(owner.PhoneNumber))
            return;
        await smsSender.SendSmsAsync(new SmsRequest(owner.PhoneNumber, msg), ct);
    }

    private async Task NotifyOwnerRejectedAsync(
        Document document,
        string approverName,
        string? comment,
        CancellationToken ct)
    {
        if (document.OwnerUserId == Guid.Empty) return;

        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        var owner = await userManager.FindByIdAsync(document.OwnerUserId.ToString());
        if (owner is null) return;

        var refPart = string.IsNullOrWhiteSpace(document.ReferenceNumber)
            ? ""
            : $" (شماره ارجاع: {document.ReferenceNumber})";
        var note = string.IsNullOrWhiteSpace(comment) ? "" : $"\nتوضیح: {comment.Trim()}";
        var msg =
            $"سند «{document.Title}»{refPart} در گردش تأیید رد شد.\n" +
            $"ردکننده: {approverName}{note}";

        await inbox.SendToUserAsync(document.OwnerUserId, "رد گردش سند", msg, ct);
        if (!smsSettings.DocumentWorkflowRejectedOwnerSmsEnabled || string.IsNullOrWhiteSpace(owner.PhoneNumber))
            return;
        await smsSender.SendSmsAsync(new SmsRequest(owner.PhoneNumber, msg), ct);
    }

    private static string FormatPersonLabel(AppUser? user, string? fallbackName)
    {
        var full = user is null ? (fallbackName ?? "").Trim() : $"{user.FirstName} {user.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(full)) full = (fallbackName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(full)) return "تأییدکننده";
        return full;
    }

    public static List<ApprovalStepDto> DeserializeSteps(string? json) =>
        WorkflowStepJsonHelper.Deserialize(json);
}
