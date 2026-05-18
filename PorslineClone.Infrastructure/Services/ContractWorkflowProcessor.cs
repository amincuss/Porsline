using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public sealed record WorkflowActionResult(bool Success, string? Message, int? HttpStatus = null)
{
    public static WorkflowActionResult Ok(string message) => new(true, message);
    public static WorkflowActionResult Fail(string message, int status = 400) => new(false, message, status);
}

public class ContractWorkflowProcessor(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ContractApprovalStampService approvalStamp,
    ContractApprovalLinkService approvalLinks,
    ISmsSender smsSender,
    IFrontendUrlResolver frontendUrls)
{
    public async Task<WorkflowActionResult> ProcessActionAsync(
        Guid contractId,
        Guid assigneeUserId,
        bool approve,
        string? comment,
        CancellationToken ct = default)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(x => x.Id == contractId, ct);
        if (contract is null) return WorkflowActionResult.Fail("قرارداد یافت نشد", 404);

        var steps = DeserializeSteps(contract.StepsJson);
        var current = steps.FirstOrDefault(s => s.Order == contract.CurrentStepOrder && s.Status == "pending");
        if (current is null) return WorkflowActionResult.Fail("مرحله فعالی برای این قرارداد وجود ندارد");
        if (current.UserId != assigneeUserId) return WorkflowActionResult.Fail("این مرحله به شما ارجاع نشده است", 403);

        var currentUser = await db.Users
            .Include(u => u.UserPosition)
            .FirstOrDefaultAsync(u => u.Id == assigneeUserId, ct);
        var approverName = currentUser is null
            ? current.UserName
            : $"{currentUser.FirstName} {currentUser.LastName}".Trim();

        if (approve)
        {
            current.Status = "approved";
            current.Comment = comment;
            current.ActionAt = DateTime.UtcNow;
            await RebuildSignedDocumentAsync(contract, steps, ct);

            var next = steps.Where(s => s.Order > current.Order).OrderBy(s => s.Order).FirstOrDefault();
            if (next is null)
            {
                contract.Status = ContractStatus.Approved;
                await SendPartyApprovedSmsAsync(contract, ct);
            }
            else
            {
                next.Status = "pending";
                contract.CurrentStepOrder = next.Order;
                contract.Status = ContractStatus.InProgress;
                await SendAssigneeSmsAsync(contract, next.UserId, approverName, current.UserName, ct);
            }

            await SendCreatorApprovalNotifySmsAsync(contract, currentUser, next, ct);
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
                    contract.Status = ContractStatus.Rejected;
                else
                {
                    next.Status = "pending";
                    contract.CurrentStepOrder = next.Order;
                    contract.Status = ContractStatus.InProgress;
                    await SendAssigneeSmsAsync(contract, next.UserId, approverName, current.UserName, ct);
                }
            }
            else
            {
                foreach (var later in steps.Where(s => s.Order > current.Order && s.Status == "waiting"))
                    later.Status = "skipped";
                contract.Status = ContractStatus.Rejected;
            }
        }

        contract.StepsJson = JsonSerializer.Serialize(steps);
        await db.SaveChangesAsync(ct);
        return WorkflowActionResult.Ok(approve ? "تأیید شد" : "رد شد");
    }

    public async Task<WorkflowActionResult> ResendPendingApprovalSmsAsync(Guid contractId, CancellationToken ct = default)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(x => x.Id == contractId, ct);
        if (contract is null) return WorkflowActionResult.Fail("قرارداد یافت نشد", 404);
        if (contract.IsArchived) return WorkflowActionResult.Fail("قرارداد بایگانی‌شده است");
        if (contract.WorkflowStartedAtUtc is null)
            return WorkflowActionResult.Fail("گردش این قرارداد هنوز شروع نشده است");
        if (contract.Status != ContractStatus.InProgress)
            return WorkflowActionResult.Fail("گردش در حال اجرا نیست یا به پایان رسیده است");

        var steps = DeserializeSteps(contract.StepsJson);
        var current = steps.FirstOrDefault(s => s.Order == contract.CurrentStepOrder && s.Status == "pending");
        if (current is null)
            return WorkflowActionResult.Fail("در حال حاضر مرحله‌ای برای تأیید فعال نیست");

        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ApprovalReferralSmsEnabled)
            return WorkflowActionResult.Fail("پیامک ارجاع تأیید در تنظیمات سیستم غیرفعال است");

        var user = await userManager.FindByIdAsync(current.UserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber))
            return WorkflowActionResult.Fail("شماره موبایل تأییدکننده فعلی ثبت نشده است");

        var sent = await SendAssigneeSmsAsync(contract, current.UserId, null, "پنل مدیریت", ct, isReminder: true);
        if (!sent)
            return WorkflowActionResult.Fail("ارسال پیامک با خطا مواجه شد. تنظیمات درگاه پیامک را بررسی کنید");

        var name = $"{user.FirstName} {user.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(name)) name = user.UserName ?? "تأییدکننده";
        return WorkflowActionResult.Ok($"پیامک یادآوری به {name} ({user.PhoneNumber}) ارسال شد");
    }

    public async Task<(bool Ok, string? Error)> TryStartWorkflowAsync(Contract contract, CancellationToken ct = default)
    {
        if (contract.WorkflowStartedAtUtc is not null)
            return (false, "گردش قبلاً شروع شده است");
        if (contract.Status != ContractStatus.Pending)
            return (false, "گردش در وضعیت انتظار نیست");

        var steps = DeserializeSteps(contract.StepsJson);
        if (steps.Count == 0)
            return (false, "مرحله‌ای برای گردش وجود ندارد");

        var signatureCount = await ContractWorkflowSignatureValidator.CountSignatureFieldsAsync(
            db,
            contract.ContractDocumentTemplateId,
            contract.ContractDocumentTemplateVersionId,
            ct);
        var signatureError = ContractWorkflowSignatureValidator.ValidateCounts(
            signatureCount,
            steps.Count(s => s.UserId != Guid.Empty));
        if (signatureError is not null)
            return (false, signatureError);

        var first = steps.OrderBy(s => s.Order).First();
        first.Status = "pending";
        contract.CurrentStepOrder = first.Order;
        contract.Status = ContractStatus.InProgress;
        contract.WorkflowStartedAtUtc = DateTime.UtcNow;
        contract.WorkflowScheduledStartAtUtc = null;
        contract.StepsJson = JsonSerializer.Serialize(steps);
        await db.SaveChangesAsync(ct);

        await SendAssigneeSmsAsync(contract, first.UserId, null, null, ct);
        return (true, null);
    }

    public async Task RebuildSignedDocumentAsync(Contract contract, List<ApprovalStepDto> steps, CancellationToken ct)
    {
        var original = await ResolveOriginalFilePathAsync(contract, ct);
        if (string.IsNullOrWhiteSpace(original)) return;

        var approved = steps.Where(s => s.Status == "approved").OrderBy(s => s.Order).ToList();
        if (approved.Count == 0) return;

        var signatories = new List<ContractSignatoryStamp>();
        foreach (var step in approved)
        {
            var user = await db.Users
                .Include(u => u.UserPosition)
                .FirstOrDefaultAsync(u => u.Id == step.UserId, ct);
            if (user is null || string.IsNullOrWhiteSpace(user.SignatureImagePath)) continue;
            var fullName = $"{user.FirstName} {user.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(fullName)) continue;
            signatories.Add(new ContractSignatoryStamp(user.SignatureImagePath, fullName, user.UserPosition?.Name));
        }

        if (signatories.Count == 0) return;
        if (!approvalStamp.TryRebuildSignedPdf(original, signatories, out var newPath)
            || string.IsNullOrWhiteSpace(newPath))
            return;

        contract.FilePath = newPath;
        if (!string.IsNullOrWhiteSpace(contract.FileName) && newPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            contract.FileName = Path.ChangeExtension(contract.FileName, ".pdf");

        var latest = await db.ContractVersions
            .Where(v => v.ContractId == contract.Id)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (latest is not null)
        {
            latest.FilePath = newPath;
            if (!string.IsNullOrWhiteSpace(latest.FileName) && newPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                latest.FileName = Path.ChangeExtension(latest.FileName, ".pdf");
        }
    }

    public static async Task<string?> ResolveOriginalFilePathAsync(Contract contract, AppDbContext dbContext, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(contract.OriginalFilePath)
            && !ContractApprovalStampService.IsSignedDocumentPath(contract.OriginalFilePath))
            return contract.OriginalFilePath;

        var v1 = await dbContext.ContractVersions
            .Where(v => v.ContractId == contract.Id)
            .OrderBy(v => v.VersionNumber)
            .Select(v => v.FilePath)
            .FirstOrDefaultAsync(ct);
        if (!string.IsNullOrWhiteSpace(v1) && !ContractApprovalStampService.IsSignedDocumentPath(v1))
            return v1;

        var fp = contract.FilePath;
        return ContractApprovalStampService.IsSignedDocumentPath(fp) ? null : fp;
    }

    private Task<string?> ResolveOriginalFilePathAsync(Contract contract, CancellationToken ct)
        => ResolveOriginalFilePathAsync(contract, db, ct);

    private async Task<bool> SendAssigneeSmsAsync(
        Contract contract,
        Guid userId,
        string? approverDisplayName,
        string? fallbackApproverName,
        CancellationToken ct,
        bool isReminder = false)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ApprovalReferralSmsEnabled) return false;

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber)) return false;

        var sender = !string.IsNullOrWhiteSpace(approverDisplayName) ? approverDisplayName : fallbackApproverName;
        if (string.IsNullOrWhiteSpace(sender)) sender = "سیستم";

        var code = await approvalLinks.CreateOrRefreshAsync(contract.Id, userId, ct);
        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var linkPath = string.IsNullOrWhiteSpace(publicBase)
            ? $"/approve/contract?c={code}"
            : $"{publicBase.TrimEnd('/')}/approve/contract?c={code}";

        var msg = isReminder
            ? $"یادآوری: قرارداد شماره «{contract.ContractNumber}» همچنان منتظر تأیید شماست.\n" +
              $"لینک تأیید (بدون نیاز به ورود):\n{linkPath}"
            : $"قرارداد شماره «{contract.ContractNumber}» برای تأیید شما ارسال شد.\n" +
              $"ارجاع‌دهنده: {sender}\n" +
              $"لینک تأیید (بدون نیاز به ورود):\n{linkPath}";

        return await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
    }

    private async Task SendCreatorApprovalNotifySmsAsync(
        Contract contract,
        AppUser? approver,
        ApprovalStepDto? nextStep,
        CancellationToken ct)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ContractCreatorApprovalNotifySmsEnabled) return;

        var creator = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == contract.CreatedByUserId, ct);
        if (creator is null || string.IsNullOrWhiteSpace(creator.PhoneNumber)) return;

        var approverLabel = FormatPersonLabel(approver, null);
        var subject = ResolveContractSubjectLabel(contract);

        var tehran = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"));
        var dateStr = tehran.ToString("yyyy/MM/dd");
        var timeStr = tehran.ToString("HH:mm");

        string statusTail;
        if (nextStep is not null)
        {
            var nextUser = await db.Users
                .AsNoTracking()
                .Include(u => u.UserPosition)
                .FirstOrDefaultAsync(u => u.Id == nextStep.UserId, ct);
            var nextLabel = FormatPersonLabel(nextUser, nextStep.UserName);
            var position = nextUser?.UserPosition?.Name?.Trim();
            statusTail = string.IsNullOrWhiteSpace(position)
                ? $"سیستم منتظر تأیید {nextLabel} در آینده است."
                : $"سیستم منتظر تأیید {nextLabel} با سمت «{position}» در آینده است.";
        }
        else
        {
            statusTail = "گردش تأیید این قرارداد به پایان رسید.";
        }

        var msg =
            $"قرارداد شماره «{contract.ContractNumber}» با موضوع «{subject}»:\n" +
            $"{approverLabel} در تاریخ {dateStr} ساعت {timeStr} تأیید کرد.\n" +
            statusTail;

        await smsSender.SendSmsAsync(new SmsRequest(creator.PhoneNumber, msg), ct);
    }

    private static string ResolveContractSubjectLabel(Contract contract)
    {
        var title = contract.Title?.Trim();
        if (!string.IsNullOrWhiteSpace(title)) return title;
        var person = contract.SubjectPersonName?.Trim();
        if (!string.IsNullOrWhiteSpace(person)) return person;
        return "بدون موضوع";
    }

    private static string FormatPersonLabel(AppUser? user, string? fallbackName)
    {
        var full = user is null ? (fallbackName ?? "").Trim() : $"{user.FirstName} {user.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(full)) full = (fallbackName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(full)) return "آقای/خانم تأییدکننده";
        if (full.StartsWith("آقای ", StringComparison.Ordinal) || full.StartsWith("خانم ", StringComparison.Ordinal))
            return full;
        return $"آقای {full}";
    }

    private async Task SendPartyApprovedSmsAsync(Contract contract, CancellationToken ct)
    {
        var phone = NormalizeDigits(contract.Phone ?? "");
        if (!Regex.IsMatch(phone, @"^09\d{9}$")) return;

        var tehran = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time"));
        var dateStr = tehran.ToString("yyyy/MM/dd");
        var timeStr = tehran.ToString("HH:mm");

        var msg =
            $"قرارداد شما با شماره سند «{contract.ContractNumber}» در تاریخ {dateStr} ساعت {timeStr} تأیید شد.";
        await smsSender.SendSmsAsync(new SmsRequest(phone, msg), ct);
    }

    private static string NormalizeDigits(string value)
        => value
            .Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
            .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
            .Trim();

    public static List<ApprovalStepDto> DeserializeSteps(string? json)
        => string.IsNullOrWhiteSpace(json) ? [] : (JsonSerializer.Deserialize<List<ApprovalStepDto>>(json) ?? []);
}
