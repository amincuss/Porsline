using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

/// <summary>یادآوری خودکار پیامک تأیید پس از مهلت (قرارداد و فرم).</summary>
public class ApprovalReminderService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ISmsSender smsSender,
    IInboxMessageService inbox,
    IFrontendUrlResolver frontendUrls)
{
    public async Task<int> ProcessDueRemindersAsync(CancellationToken ct = default)
    {
        var settings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!settings.ApprovalReminderSmsEnabled || !settings.ApprovalReferralSmsEnabled)
            return 0;

        var now = DateTime.UtcNow;
        var sent = 0;

        sent += await ProcessContractRemindersAsync(settings, now, ct);
        sent += await ProcessContractWorkflowValidityAsync(settings, now, ct);
        sent += await ProcessFormRemindersAsync(settings, now, ct);

        if (sent > 0)
            await db.SaveChangesAsync(ct);

        return sent;
    }

    private async Task<int> ProcessContractRemindersAsync(SmsSettings settings, DateTime now, CancellationToken ct)
    {
        var links = await db.ContractApprovalLinks
            .Include(x => x.Contract)
            .Where(x => x.IsActive
                        && x.ReminderSmsSentAtUtc == null
                        && x.ExpiresAtUtc > now)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var link in links)
        {
            var contract = link.Contract;
            if (contract.IsArchived || contract.Status != ContractStatus.InProgress)
                continue;
            if (ContractAmendmentHelper.IsActive(ContractAmendmentHelper.Deserialize(contract.AmendmentJson)))
                continue;

            var steps = ContractWorkflowProcessor.DeserializeSteps(contract.StepsJson);
            var current = steps.FirstOrDefault(s => s.Order == contract.CurrentStepOrder && s.Status == "pending");
            if (current is null || current.UserId != link.AssigneeUserId)
                continue;

            var deadline = ApprovalDeadlineHelper.ResolveDeadline(current, settings);
            if (!ApprovalDeadlineHelper.IsDue(link.CreatedAtUtc, deadline, now))
                continue;

            if (await SendContractReminderAsync(contract, link, deadline, ct))
            {
                link.ReminderSmsSentAtUtc = now;
                sent++;
            }
        }

        return sent;
    }

    private async Task<int> ProcessContractWorkflowValidityAsync(SmsSettings settings, DateTime now, CancellationToken ct)
    {
        if (!settings.WorkflowValidityReminderSmsEnabled || !settings.ApprovalReferralSmsEnabled)
            return 0;

        var contracts = await db.Contracts
            .Where(c => c.Status == ContractStatus.InProgress
                        && c.WorkflowValidityEndsAtUtc != null
                        && c.WorkflowStartedAtUtc != null
                        && !c.IsArchived)
            .ToListAsync(ct);

        var grace = WorkflowValidityHelper.ResolveSuspensionGrace(settings);
        var sent = 0;

        foreach (var contract in contracts)
        {
            if (contract.WorkflowValidityEndsAtUtc is null) continue;
            if (ContractAmendmentHelper.IsActive(ContractAmendmentHelper.Deserialize(contract.AmendmentJson)))
                continue;

            var steps = ContractWorkflowProcessor.DeserializeSteps(contract.StepsJson);
            var current = steps.FirstOrDefault(s => s.Order == contract.CurrentStepOrder && s.Status == "pending");
            if (current is null) continue;

            var link = await db.ContractApprovalLinks
                .FirstOrDefaultAsync(x => x.ContractId == contract.Id
                                          && x.AssigneeUserId == current.UserId
                                          && x.IsActive
                                          && x.ExpiresAtUtc > now, ct);
            if (link is null) continue;

            if (now >= contract.WorkflowValidityEndsAtUtc.Value
                && contract.WorkflowValidityReminderSentAtUtc is null)
            {
                if (await SendContractWorkflowValidityReminderAsync(contract, link, current, ct))
                {
                    contract.WorkflowValidityReminderSentAtUtc = now;
                    sent++;
                }
                continue;
            }

            var suspendAt = contract.WorkflowValidityEndsAtUtc.Value + grace;
            if (now < suspendAt) continue;
            if (contract.Status != ContractStatus.InProgress) continue;

            contract.Status = ContractStatus.Suspended;
            contract.SuspendedPendingUserId = current.UserId;
            WorkflowEventHelper.Append(contract, new WorkflowEventDto
            {
                Kind = "workflow_suspended",
                StepOrder = current.Order,
                ActorUserId = current.UserId,
                ActorName = current.UserName,
                Comment = "تعلیق خودکار پس از اتمام اعتبار گردش و مهلت تکمیلی",
                AtUtc = now
            });
            sent++;
        }

        return sent;
    }

    private async Task<bool> SendContractWorkflowValidityReminderAsync(
        Contract contract,
        ContractApprovalLink link,
        ApprovalStepDto current,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(link.AssigneeUserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return false;

        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var linkPath = string.IsNullOrWhiteSpace(publicBase)
            ? $"/approve/contract?c={link.Code}"
            : $"{publicBase.TrimEnd('/')}/approve/contract?c={link.Code}";

        var subject = string.IsNullOrWhiteSpace(contract.SubjectPersonName)
            ? contract.Title
            : contract.SubjectPersonName;
        var msg =
            $"یادآوری: قرارداد شماره «{contract.ContractNumber}» با موضوع «{subject}» هنوز توسط شما امضا/تأیید نشده است.\n" +
            $"اعتبار گردش کار به پایان رسیده و از مهلت مقرر تأخیر دارید.\n" +
            $"لینک سریع امضا و تأیید:\n{linkPath}";

        await inbox.SendToUserAsync(link.AssigneeUserId, "یادآوری تأخیر اعتبار گردش قرارداد", msg, ct);
        return await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
    }

    private async Task<int> ProcessFormRemindersAsync(SmsSettings settings, DateTime now, CancellationToken ct)
    {
        var links = await db.FormSubmissionApprovalLinks
            .Include(x => x.FormSubmission)
            .ThenInclude(x => x.Form)
            .Where(x => x.IsActive
                        && x.ReminderSmsSentAtUtc == null
                        && x.ExpiresAtUtc > now)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var link in links)
        {
            var submission = link.FormSubmission;
            if (submission.Status != FormSubmissionStatus.InProgress)
                continue;

            var steps = FormWorkflowProcessor.DeserializeSteps(submission.StepsJson);
            var current = steps.FirstOrDefault(s => s.Order == submission.CurrentStepOrder && s.Status == "pending");
            if (current is null || current.UserId != link.AssigneeUserId)
                continue;

            var deadline = ApprovalDeadlineHelper.ResolveDeadline(current, settings);
            if (!ApprovalDeadlineHelper.IsDue(link.CreatedAtUtc, deadline, now))
                continue;

            if (await SendFormReminderAsync(submission, link, deadline, ct))
            {
                link.ReminderSmsSentAtUtc = now;
                sent++;
            }
        }

        return sent;
    }

    private async Task<bool> SendContractReminderAsync(
        Contract contract,
        ContractApprovalLink link,
        TimeSpan deadline,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(link.AssigneeUserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return false;

        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var linkPath = string.IsNullOrWhiteSpace(publicBase)
            ? $"/approve/contract?c={link.Code}"
            : $"{publicBase.TrimEnd('/')}/approve/contract?c={link.Code}";

        var deadlineLabel = ApprovalDeadlineHelper.FormatDeadlineFa(deadline);
        var msg =
            $"تأخیر در تأیید: مهلت ({deadlineLabel}) برای قرارداد «{contract.ContractNumber}» به پایان رسیده و هنوز تأیید شما ثبت نشده است.\n" +
            $"لطفاً هرچه سریع‌تر بررسی کنید.\n" +
            $"لینک تأیید (بدون نیاز به ورود):\n{linkPath}";

        await inbox.SendToUserAsync(link.AssigneeUserId, "یادآوری تأخیر تأیید قرارداد", msg, ct);
        return await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
    }

    private async Task<bool> SendFormReminderAsync(
        FormSubmission submission,
        FormSubmissionApprovalLink link,
        TimeSpan deadline,
        CancellationToken ct)
    {
        var user = await userManager.FindByIdAsync(link.AssigneeUserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return false;

        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);
        var linkPath = string.IsNullOrWhiteSpace(publicBase)
            ? $"/approve/form?c={link.Code}"
            : $"{publicBase.TrimEnd('/')}/approve/form?c={link.Code}";
        var adminApprovals = string.IsNullOrWhiteSpace(adminBase)
            ? "/admin/approvals"
            : $"{adminBase.TrimEnd('/')}/admin/approvals";

        var formTitle = submission.Form?.Title ?? "فرم";
        var deadlineLabel = ApprovalDeadlineHelper.FormatDeadlineFa(deadline);
        var msg =
            $"تأخیر در تأیید: مهلت ({deadlineLabel}) برای پاسخ فرم «{formTitle}» به پایان رسیده و هنوز تأیید شما ثبت نشده است.\n" +
            $"لینک تأیید: {linkPath}\n" +
            $"یا پنل: {adminApprovals}";

        await inbox.SendToUserAsync(link.AssigneeUserId, "یادآوری تأخیر تأیید فرم", msg, ct);
        return await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
    }

    public static async Task MarkReminderSentForContractAsync(
        AppDbContext dbContext,
        Guid contractId,
        Guid assigneeUserId,
        CancellationToken ct)
    {
        var link = await dbContext.ContractApprovalLinks
            .FirstOrDefaultAsync(x => x.ContractId == contractId && x.AssigneeUserId == assigneeUserId && x.IsActive, ct);
        if (link is null) return;
        link.ReminderSmsSentAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);
    }

    public static async Task MarkReminderSentForFormAsync(
        AppDbContext dbContext,
        Guid submissionId,
        Guid assigneeUserId,
        CancellationToken ct)
    {
        var link = await dbContext.FormSubmissionApprovalLinks
            .FirstOrDefaultAsync(x => x.FormSubmissionId == submissionId && x.AssigneeUserId == assigneeUserId && x.IsActive, ct);
        if (link is null) return;
        link.ReminderSmsSentAtUtc = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(ct);
    }
}
