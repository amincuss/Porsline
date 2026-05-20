using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class FormWorkflowProcessor(
    AppDbContext db,
    UserManager<AppUser> userManager,
    FormSubmissionApprovalLinkService approvalLinks,
    ISmsSender smsSender,
    IInboxMessageService inbox,
    IFrontendUrlResolver frontendUrls)
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
        var current = steps.FirstOrDefault(s => s.Order == submission.CurrentStepOrder && s.Status == "pending");
        if (current is null) return WorkflowActionResult.Fail("مرحله فعالی برای این پاسخ وجود ندارد");
        if (current.UserId != assigneeUserId) return WorkflowActionResult.Fail("این مرحله به شما ارجاع نشده است", 403);

        var currentUser = await db.Users.FirstOrDefaultAsync(u => u.Id == assigneeUserId, ct);
        var approverName = currentUser is null
            ? current.UserName
            : $"{currentUser.FirstName} {currentUser.LastName}".Trim();

        if (approve)
        {
            current.Status = "approved";
            current.Comment = comment;
            current.ActionAt = DateTime.UtcNow;

            var next = steps.Where(s => s.Order > current.Order).OrderBy(s => s.Order).FirstOrDefault();
            if (next is null)
                submission.Status = FormSubmissionStatus.Approved;
            else
            {
                next.Status = "pending";
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
                var next = steps.Where(s => s.Order > current.Order).OrderBy(s => s.Order).FirstOrDefault();
                if (next is null)
                    submission.Status = FormSubmissionStatus.Rejected;
                else
                {
                    next.Status = "pending";
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
            }
        }

        submission.StepsJson = JsonSerializer.Serialize(steps);
        await db.SaveChangesAsync(ct);

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
        var current = steps.FirstOrDefault(s => s.Order == submission.CurrentStepOrder && s.Status == "pending");
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
        first.Status = "pending";
        submission.CurrentStepOrder = first.Order;
        submission.Status = FormSubmissionStatus.InProgress;
        submission.WorkflowStartedAtUtc = DateTime.UtcNow;
        submission.WorkflowScheduledStartAtUtc = null;
        submission.StepsJson = JsonSerializer.Serialize(steps);
        await db.SaveChangesAsync(ct);

        await SendAssigneeSmsAsync(submission, first.UserId, null, null, ct);
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
        var adminApprovals = string.IsNullOrWhiteSpace(adminBase)
            ? "/admin/approvals"
            : $"{adminBase.TrimEnd('/')}/admin/approvals";

        var formTitle = submission.Form?.Title ?? "فرم";
        var msg = isReminder
            ? $"یادآوری: پاسخ فرم «{formTitle}» همچنان منتظر تأیید شماست.\n" +
              $"لینک تأیید: {linkPath}\n" +
              $"یا پنل: {adminApprovals}"
            : $"پاسخ جدید از فرم «{formTitle}» برای تأیید شما ارسال شد.\n" +
              $"ارجاع‌دهنده: {sender}\n" +
              $"لینک تأیید: {linkPath}\n" +
              $"یا پنل: {adminApprovals}";

        var inboxTitle = isReminder ? "یادآوری تأیید فرم" : "فرم برای تأیید";
        await inbox.SendToUserAsync(userId, inboxTitle, msg, ct);
        if (!smsSettings.ApprovalReferralSmsEnabled || string.IsNullOrWhiteSpace(user.PhoneNumber)) return false;
        return await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
    }

    public static List<ApprovalStepDto> DeserializeSteps(string? json)
        => string.IsNullOrWhiteSpace(json) ? [] : (JsonSerializer.Deserialize<List<ApprovalStepDto>>(json) ?? []);
}
