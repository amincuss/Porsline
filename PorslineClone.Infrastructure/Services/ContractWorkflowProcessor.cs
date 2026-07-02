using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Application.Users;
using PorslineClone.Application.ContractTemplates;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.ContractTemplates;
using PorslineClone.Infrastructure.Services.SmsPatterns;

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
    ContractPostApprovalService postApproval,
    ISmsSender smsSender,
    ISmsPatternService smsPatterns,
    IInboxMessageService inbox,
    IFrontendUrlResolver frontendUrls,
    IHostEnvironment hostEnvironment)
{
    public async Task<WorkflowActionResult> ProcessActionAsync(
        Guid contractId,
        Guid assigneeUserId,
        bool approve,
        string? comment,
        string? rejectionType = null,
        CancellationToken ct = default)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(x => x.Id == contractId, ct);
        if (contract is null) return WorkflowActionResult.Fail("قرارداد یافت نشد", 404);
        if (contract.IsArchived && contract.Status == ContractStatus.Rejected)
            return WorkflowActionResult.Fail("گردش با رد قطعی پایان یافته و پرونده بایگانی شده است");
        if (contract.IsArchived)
            return WorkflowActionResult.Fail("قرارداد بایگانی شده است");
        if (contract.Status == ContractStatus.Rejected)
            return WorkflowActionResult.Fail("گردش با رد قطعی پایان یافته است");
        if (contract.Status == ContractStatus.Suspended)
            return WorkflowActionResult.Fail("گردش معلق شده است. ایجادکننده می‌تواند «اتمام گردش ناتمام» را ثبت کند", 403);
        if (contract.Status == ContractStatus.Incomplete)
            return WorkflowActionResult.Fail("گردش به‌صورت ناتمام پایان یافته است", 403);

        var activeAmendment = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        if (ContractAmendmentHelper.IsActive(activeAmendment))
            return WorkflowActionResult.Fail("قرارداد در مرحله اصلاحیه است. ابتدا وضعیت اصلاحیه را به‌روزرسانی کنید.");

        var steps = DeserializeSteps(contract.StepsJson);
        var current = steps.FirstOrDefault(s => s.Order == contract.CurrentStepOrder && s.Status == "pending");
        if (current is null) return WorkflowActionResult.Fail("مرحله فعالی برای این قرارداد وجود ندارد");
        if (current.UserId != assigneeUserId) return WorkflowActionResult.Fail("این مرحله به شما ارجاع نشده است", 403);

        var rejType = ContractWorkflowRejectionTypes.Normalize(rejectionType);

        var currentUser = await db.Users
            .Include(u => u.UserPosition)
            .FirstOrDefaultAsync(u => u.Id == assigneeUserId, ct);
        var approverName = currentUser is null
            ? current.UserName
            : $"{currentUser.FirstName} {currentUser.LastName}".Trim();

        if (approve)
        {
            var signatureKeyCount = await ContractWorkflowSignatureValidator.CountSignatureFieldsAsync(
                db,
                contract.ContractDocumentTemplateId,
                contract.ContractDocumentTemplateVersionId,
                ct);
            if (signatureKeyCount > 0
                && string.IsNullOrWhiteSpace(currentUser?.SignatureImagePath))
            {
                return WorkflowActionResult.Fail(
                    "تصویر امضا در پروفایل شما ثبت نشده است. از منوی پروفایل امضا را آپلود کنید.");
            }

            current.Status = "approved";
            current.Comment = comment;
            current.ActionAt = DateTime.UtcNow;
            AppendWorkflowEvent(contract, new WorkflowEventDto
            {
                Kind = "approved",
                StepOrder = current.Order,
                ActorUserId = assigneeUserId,
                ActorName = approverName,
                Comment = comment,
                Cycle = current.ReviewCycle,
                AtUtc = current.ActionAt.Value
            });
            await RebuildSignedDocumentAsync(contract, steps, ct);

            var next = steps.Where(s => s.Order > current.Order).OrderBy(s => s.Order).FirstOrDefault();
            if (next is null)
            {
                contract.Status = ContractStatus.Approved;
                await SendPartyApprovedSmsAsync(contract, ct);
                await postApproval.TryStartPostApprovalAsync(contract, ct);
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
            current.RejectionType = rejType;
            current.ActionAt = DateTime.UtcNow;

            if (current.OnReject == "continue")
            {
                var next = steps.Where(s => s.Order > current.Order).OrderBy(s => s.Order).FirstOrDefault();
                if (next is null)
                {
                    contract.Status = ContractStatus.Rejected;
                    contract.IsArchived = true;
                    AppendWorkflowEvent(contract, new WorkflowEventDto
                    {
                        Kind = "full_rejected",
                        StepOrder = current.Order,
                        ActorUserId = assigneeUserId,
                        ActorName = approverName,
                        Comment = comment,
                        RejectionType = rejType,
                        Cycle = current.ReviewCycle,
                        AtUtc = current.ActionAt.Value
                    });
                    await SendFullRejectionResultSmsAsync(contract, steps, current, comment, ct);
                }
                else
                {
                    next.Status = "pending";
                    contract.CurrentStepOrder = next.Order;
                    contract.Status = ContractStatus.InProgress;
                    await SendAssigneeSmsAsync(contract, next.UserId, approverName, current.UserName, ct);
                }
            }
            else if (rejType == "full")
            {
                await HandleFullRejectTerminalAsync(
                    contract, steps, current, assigneeUserId, approverName, comment, ct);
            }
            else
            {
                await HandleRejectStopAsync(contract, steps, current, assigneeUserId, rejType, approverName, ct);
            }
        }

        contract.StepsJson = JsonSerializer.Serialize(steps);
        await db.SaveChangesAsync(ct);

        if (!string.IsNullOrWhiteSpace(comment))
            await NotifyCreatorAboutApproverNoteAsync(contract, assigneeUserId, approverName, comment, approve, ct);

        return WorkflowActionResult.Ok(approve ? "تأیید شد" : "رد شد");
    }

    private async Task NotifyCreatorAboutApproverNoteAsync(
        Contract contract,
        Guid assigneeUserId,
        string? approverName,
        string comment,
        bool approved,
        CancellationToken ct)
    {
        var creatorId = contract.CreatedByUserId;
        if (creatorId == Guid.Empty || creatorId == assigneeUserId) return;
        var who = string.IsNullOrWhiteSpace(approverName) ? "تأییدکننده" : approverName;
        var status = approved ? "تأیید" : "رد";
        var title = contract.Title?.Trim();
        if (string.IsNullOrWhiteSpace(title)) title = "قرارداد";
        await inbox.SendToUserAsync(
            creatorId,
            $"یادداشت تأییدکننده — {title}",
            $"پس از {status}، {who} نوشت:\n{comment.Trim()}",
            ct);
    }

    public async Task<WorkflowActionResult> UpdateAmendmentAsync(
        Guid contractId,
        Guid userId,
        string amendmentStatus,
        string? note,
        CancellationToken ct = default)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(x => x.Id == contractId, ct);
        if (contract is null) return WorkflowActionResult.Fail("قرارداد یافت نشد", 404);

        var state = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        if (!ContractAmendmentHelper.IsActive(state))
            return WorkflowActionResult.Fail("اصلاحیه فعالی برای این قرارداد وجود ندارد");

        if (!ContractAmendmentHelper.CanUserActOnAmendment(state!, userId, contract.CreatedByUserId))
            return WorkflowActionResult.Fail("به‌روزرسانی اصلاحیه فقط توسط مسئول اصلاح مجاز است", 403);

        var normalized = amendmentStatus switch
        {
            "in_progress" => "in_progress",
            "done" => "done",
            _ => "waiting"
        };

        state.AmendmentStatus = normalized;
        if (!string.IsNullOrWhiteSpace(note))
            state.AmendmentNote = note.Trim();

        if (normalized != "done")
        {
            contract.AmendmentJson = ContractAmendmentHelper.Serialize(state);
            await db.SaveChangesAsync(ct);
            return WorkflowActionResult.Ok("وضعیت اصلاحیه ذخیره شد");
        }

        if (state.Phase == "creator_amendment" && state.AmendedVersionNumber is null)
            return WorkflowActionResult.Fail("ابتدا نسخه اصلاح‌شده قرارداد را آپلود کنید، سپس «ارسال به گردش» را بزنید");

        var steps = DeserializeSteps(contract.StepsJson);
        var rejecter = steps.FirstOrDefault(s => s.Order == state.RejectedAtStepOrder);
        if (rejecter is null)
            return WorkflowActionResult.Fail("مرحله ردکننده در گردش یافت نشد");

        state.CompletedAtUtc = DateTime.UtcNow;
        var cycle = state.Cycle > 0 ? state.Cycle : WorkflowEventHelper.GetNextAmendmentCycle(contract);
        var now = DateTime.UtcNow;

        AppendWorkflowEvent(contract, new WorkflowEventDto
        {
            Kind = "amendment_completed",
            StepOrder = state.RejectedAtStepOrder,
            Comment = state.AmendmentNote,
            RejectionType = state.RejectionType,
            Cycle = cycle,
            AtUtc = now
        });
        AppendWorkflowEvent(contract, new WorkflowEventDto
        {
            Kind = "reapproval_requested",
            StepOrder = rejecter.Order,
            ActorUserId = state.AssigneeUserId,
            Comment = state.AmendmentNote,
            RejectionType = state.RejectionType,
            Cycle = cycle,
            AtUtc = now
        });
        if (state.AmendedVersionNumber is not null)
        {
            AppendWorkflowEvent(contract, new WorkflowEventDto
            {
                Kind = "amended_resubmitted",
                StepOrder = rejecter.Order,
                ActorUserId = state.AssigneeUserId,
                Comment = state.AmendmentNote,
                RejectionType = state.RejectionType,
                Cycle = cycle,
                AtUtc = now
            });
        }

        rejecter.LastRejectionComment = rejecter.Comment;
        rejecter.LastRejectionType = state.RejectionType;
        rejecter.LastRejectedAtUtc = rejecter.ActionAt ?? state.StartedAtUtc;
        rejecter.ReviewCycle = cycle;
        rejecter.Status = "pending";
        contract.AmendmentJson = null;
        contract.CurrentStepOrder = rejecter.Order;
        contract.Status = ContractStatus.InProgress;
        contract.StepsJson = JsonSerializer.Serialize(steps);
        await db.SaveChangesAsync(ct);

        try
        {
            await SendAmendmentReturnToRejecterSmsAsync(contract, rejecter, state, ct);
            await SendAssigneeSmsAsync(contract, rejecter.UserId, null, "سیستم", ct);
        }
        catch
        {
            // گردش ذخیره شده؛ خطای پیامک نباید ارسال به گردش را برای کاربر ناموفق نشان دهد
        }

        var doneMsg = state.AmendedVersionNumber is not null
            ? $"نسخه اصلاح‌شده (v{state.AmendedVersionNumber}) ارسال شد؛ پرونده برای تأیید مجدد به کارشناس ردکننده ارجاع شد"
            : "اصلاحیه تکمیل شد؛ پرونده برای تأیید مجدد به کارشناس ردکننده ارجاع شد";
        return WorkflowActionResult.Ok(doneMsg);
    }

    public async Task<WorkflowActionResult> RegisterAmendedVersionAsync(
        Guid contractId,
        Guid userId,
        int versionNumber,
        CancellationToken ct = default)
    {
        var contract = await db.Contracts.FirstOrDefaultAsync(x => x.Id == contractId, ct);
        if (contract is null) return WorkflowActionResult.Fail("قرارداد یافت نشد", 404);

        var state = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        if (!ContractAmendmentHelper.IsActive(state))
            return WorkflowActionResult.Fail("فاز اصلاحیه فعالی برای این قرارداد وجود ندارد");
        if (contract.CreatedByUserId != userId)
            return WorkflowActionResult.Fail("فقط ایجادکننده قرارداد می‌تواند نسخه اصلاح‌شده را ثبت کند", 403);

        var version = await db.ContractVersions
            .FirstOrDefaultAsync(v => v.ContractId == contractId && v.VersionNumber == versionNumber, ct);
        if (version is null || !version.IsAmendedVersion)
            return WorkflowActionResult.Fail("نسخه اصلاح‌شده یافت نشد");

        var now = DateTime.UtcNow;
        state.AmendedVersionNumber = versionNumber;
        state.AmendedFileUploadedAtUtc = now;
        contract.AmendmentJson = ContractAmendmentHelper.Serialize(state);

        var cycle = state.Cycle > 0 ? state.Cycle : WorkflowEventHelper.GetNextAmendmentCycle(contract);
        AppendWorkflowEvent(contract, new WorkflowEventDto
        {
            Kind = "amended_file_uploaded",
            StepOrder = state.RejectedAtStepOrder,
            ActorUserId = userId,
            Comment = $"نسخه اصلاح‌شده v{versionNumber}",
            RejectionType = state.RejectionType,
            Cycle = cycle,
            AtUtc = now
        });

        await db.SaveChangesAsync(ct);
        return WorkflowActionResult.Ok($"نسخه اصلاح‌شده (v{versionNumber}) ثبت شد. پس از آماده‌سازی، «ارسال به گردش» را بزنید.");
    }

    private async Task HandleRejectStopAsync(
        Contract contract,
        List<ApprovalStepDto> steps,
        ApprovalStepDto current,
        Guid rejecterUserId,
        string rejectionType,
        string approverName,
        CancellationToken ct)
    {
        var first = steps.OrderBy(s => s.Order).First();
        var returnToCreator = current.Order == first.Order;

        foreach (var s in steps.Where(x => x.Status == "pending" && x.Order != current.Order))
            s.Status = "waiting";

        var cycle = WorkflowEventHelper.GetNextAmendmentCycle(contract);
        var amendment = new ContractAmendmentStateDto
        {
            Phase = returnToCreator ? "creator_amendment" : "first_approver_amendment",
            RejectionType = rejectionType,
            AmendmentStatus = "waiting",
            AmendmentNote = current.Comment,
            RejectedAtStepOrder = current.Order,
            RejectedByUserId = rejecterUserId,
            AssigneeUserId = returnToCreator ? contract.CreatedByUserId : first.UserId,
            StartedAtUtc = DateTime.UtcNow,
            Cycle = cycle,
            SignedFilePath = HasSignedWorkflowDocument(contract) ? contract.FilePath : null,
            SignedPdfFilePath = HasSignedWorkflowDocument(contract) ? contract.PdfFilePath : null,
            SignedFileName = HasSignedWorkflowDocument(contract) ? contract.FileName : null,
        };

        contract.AmendmentJson = ContractAmendmentHelper.Serialize(amendment);
        contract.Status = ContractStatus.InProgress;
        contract.CurrentStepOrder = current.Order;

        var at = current.ActionAt ?? DateTime.UtcNow;
        AppendWorkflowEvent(contract, new WorkflowEventDto
        {
            Kind = "rejected_for_amendment",
            StepOrder = current.Order,
            ActorUserId = rejecterUserId,
            ActorName = approverName,
            Comment = current.Comment,
            RejectionType = rejectionType,
            Cycle = cycle,
            AtUtc = at
        });
        AppendWorkflowEvent(contract, new WorkflowEventDto
        {
            Kind = "amendment_started",
            StepOrder = current.Order,
            ActorUserId = amendment.AssigneeUserId,
            Comment = current.Comment,
            RejectionType = rejectionType,
            Cycle = cycle,
            AtUtc = at
        });

        var assigneeLinkCode = await approvalLinks.CreateOrRefreshAsync(contract.Id, amendment.AssigneeUserId, ct);
        await SendAmendmentAssigneeSmsAsync(contract, amendment, approverName, assigneeLinkCode, ct);
        await SendRejectionNotifySmsAsync(contract, current, amendment, approverName, ct);
    }

    private async Task HandleFullRejectTerminalAsync(
        Contract contract,
        List<ApprovalStepDto> steps,
        ApprovalStepDto current,
        Guid rejecterUserId,
        string rejecterName,
        string? comment,
        CancellationToken ct)
    {
        foreach (var s in steps.Where(x => x.Status is "pending" or "waiting"))
            s.Status = "skipped";

        contract.Status = ContractStatus.Rejected;
        contract.IsArchived = true;
        contract.AmendmentJson = null;
        contract.CurrentStepOrder = current.Order;

        AppendWorkflowEvent(contract, new WorkflowEventDto
        {
            Kind = "full_rejected",
            StepOrder = current.Order,
            ActorUserId = rejecterUserId,
            ActorName = rejecterName,
            Comment = comment,
            RejectionType = "full",
            Cycle = current.ReviewCycle,
            AtUtc = current.ActionAt ?? DateTime.UtcNow
        });

        await SendFullRejectionResultSmsAsync(contract, steps, current, comment, ct);
    }

    private async Task SendFullRejectionResultSmsAsync(
        Contract contract,
        List<ApprovalStepDto> steps,
        ApprovalStepDto rejectedStep,
        string? comment,
        CancellationToken ct)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ContractRejectionNotifySmsEnabled) return;

        var recipientIds = steps.Select(s => s.UserId)
            .Append(contract.CreatedByUserId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var users = await db.Users.AsNoTracking()
            .Where(u => recipientIds.Contains(u.Id))
            .ToListAsync(ct);

        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var commentLine = string.IsNullOrWhiteSpace(comment)
            ? ""
            : $"\nیادداشت: {comment.Trim()}";

        foreach (var user in users)
        {
            if (string.IsNullOrWhiteSpace(user.PhoneNumber)) continue;

            var name = FormatPersonLabel(user, null);
            var code = await approvalLinks.CreateOrRefreshAsync(contract.Id, user.Id, ct);
            var linkPath = string.IsNullOrWhiteSpace(publicBase)
                ? $"/approve/contract?c={code}"
                : $"{publicBase.TrimEnd('/')}/approve/contract?c={code}";

            var msg = await smsPatterns.RenderAsync("contract.rejection.full.final", SmsPatternVars.Dict(
                ("recipientName", name),
                ("contractNumber", contract.ContractNumber),
                ("stepOrder", rejectedStep.Order.ToString()),
                ("stepUserName", rejectedStep.UserName ?? ""),
                ("commentLine", commentLine),
                ("linkPath", linkPath)
            ), ct);

            await inbox.SendToUserAsync(user.Id, "رد قطعی قرارداد", msg, ct);
            await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
        }
    }

    private static void AppendWorkflowEvent(Contract contract, WorkflowEventDto evt)
        => WorkflowEventHelper.Append(contract, evt);

    private async Task SendAmendmentAssigneeSmsAsync(
        Contract contract,
        ContractAmendmentStateDto amendment,
        string rejecterName,
        string approveLinkCode,
        CancellationToken ct)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ContractAmendmentAssigneeSmsEnabled) return;

        var assignee = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == amendment.AssigneeUserId, ct);
        if (assignee is null || string.IsNullOrWhiteSpace(assignee.PhoneNumber)) return;

        var typeLabel = ContractAmendmentHelper.RejectionTypeLabel(amendment.RejectionType);
        var target = amendment.Phase == "creator_amendment"
            ? "شما به‌عنوان ایجادکننده قرارداد"
            : "شما به‌عنوان تأییدکننده اول";

        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var amendPath = string.IsNullOrWhiteSpace(publicBase)
            ? $"/approve/contract?c={approveLinkCode}"
            : $"{publicBase.TrimEnd('/')}/approve/contract?c={approveLinkCode}";

        var msg = await smsPatterns.RenderAsync("contract.amendment.assignee", SmsPatternVars.Dict(
            ("contractNumber", contract.ContractNumber),
            ("rejecterName", rejecterName),
            ("rejectionTypeLabel", typeLabel),
            ("targetRole", target),
            ("amendPath", amendPath)
        ), ct);

        await inbox.SendToUserAsync(amendment.AssigneeUserId, "اصلاحیه قرارداد", msg, ct);
        await smsSender.SendSmsAsync(new SmsRequest(assignee.PhoneNumber, msg), ct);
    }

    private async Task SendAmendmentReturnToRejecterSmsAsync(
        Contract contract,
        ApprovalStepDto rejecterStep,
        ContractAmendmentStateDto state,
        CancellationToken ct)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ContractAmendmentReturnToRejecterSmsEnabled) return;

        var user = await userManager.FindByIdAsync(rejecterStep.UserId.ToString());
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber)) return;

        var typeLabel = ContractAmendmentHelper.RejectionTypeLabel(state.RejectionType);
        var versionLine = state.AmendedVersionNumber is not null
            ? $"نسخه اصلاح‌شده (v{state.AmendedVersionNumber}) آماده بررسی است.\n"
            : "";
        var msg = await smsPatterns.RenderAsync("contract.amendment.return.rejecter", SmsPatternVars.Dict(
            ("contractNumber", contract.ContractNumber),
            ("rejectionTypeLabel", typeLabel),
            ("versionLine", versionLine)
        ), ct);

        var title = state.AmendedVersionNumber is not null ? "نسخه اصلاح‌شده — تأیید مجدد" : "بازگشت برای تأیید مجدد";
        await inbox.SendToUserAsync(rejecterStep.UserId, title, msg, ct);
        await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
    }

    private async Task SendRejectionNotifySmsAsync(
        Contract contract,
        ApprovalStepDto rejectedStep,
        ContractAmendmentStateDto amendment,
        string rejecterName,
        CancellationToken ct)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ContractRejectionNotifySmsEnabled) return;

        if (contract.CreatedByUserId == amendment.AssigneeUserId) return;

        var creator = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == contract.CreatedByUserId, ct);
        if (creator is null || string.IsNullOrWhiteSpace(creator.PhoneNumber)) return;

        var typeLabel = ContractAmendmentHelper.RejectionTypeLabel(amendment.RejectionType);
        var commentBlock = rejectedStep.Comment is { Length: > 0 } c
            ? $"\nیادداشت: {c}"
            : "";
        var msg = await smsPatterns.RenderAsync("contract.rejection.notify.creator", SmsPatternVars.Dict(
            ("contractNumber", contract.ContractNumber),
            ("stepOrder", rejectedStep.Order.ToString()),
            ("rejecterName", rejecterName),
            ("rejectionTypeLabel", typeLabel),
            ("commentBlock", commentBlock)
        ), ct);

        await inbox.SendToUserAsync(contract.CreatedByUserId, "رد قرارداد", msg, ct);
        await smsSender.SendSmsAsync(new SmsRequest(creator.PhoneNumber, msg), ct);
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

        ContractWorkflowTemplate? wfTemplate = null;
        if (contract.WorkflowTemplateId is not null)
            wfTemplate = await db.ContractWorkflowTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == contract.WorkflowTemplateId, ct);
        WorkflowValidityHelper.ApplyValidityOnWorkflowStart(contract, wfTemplate);

        await db.SaveChangesAsync(ct);

        await SendAssigneeSmsAsync(contract, first.UserId, null, null, ct);
        return (true, null);
    }

    public async Task RebuildSignedDocumentAsync(Contract contract, List<ApprovalStepDto> steps, CancellationToken ct)
    {
        var pristinePath = await ResolvePristineSourcePathAsync(contract, ct);
        if (string.IsNullOrWhiteSpace(pristinePath))
            return;

        var workPath = ResolveWorkFilePath(contract, pristinePath);

        var signatureKeys = await ContractWorkflowSignatureValidator.GetOrderedSignatureFieldKeysAsync(
            db,
            contract.ContractDocumentTemplateId,
            contract.ContractDocumentTemplateVersionId,
            ct);

        var pristineFull = UserSignatureStorageService.ResolveFullPath(
            hostEnvironment, pristinePath);
        var keysInDoc = File.Exists(pristineFull)
            ? ContractSignatureDocumentWriter.ScanPlaceholderKeys(pristineFull)
            : [];

        var slots = await BuildSignatureSlotsAsync(steps, signatureKeys, keysInDoc, ct);
        if (slots.Count == 0)
            return;

        if (!approvalStamp.TryRewriteContractFile(workPath, pristinePath, slots, out _))
            return;

        contract.FilePath = workPath;
    }

    /// <summary>نسخهٔ بدون امضا — اول ContractVersions نسخه ۱ (هرگز با امضا عوض نمی‌شود).</summary>
    private async Task<string?> ResolvePristineSourcePathAsync(Contract contract, CancellationToken ct)
    {
        var v1 = await db.ContractVersions
            .AsNoTracking()
            .Where(v => v.ContractId == contract.Id)
            .OrderBy(v => v.VersionNumber)
            .Select(v => v.FilePath)
            .FirstOrDefaultAsync(ct);

        string? source = null;
        if (!string.IsNullOrWhiteSpace(v1) && !ContractApprovalStampService.IsSignedDocumentPath(v1))
            source = v1;
        else if (!string.IsNullOrWhiteSpace(contract.OriginalFilePath)
                 && !ContractApprovalStampService.IsSignedDocumentPath(contract.OriginalFilePath))
            source = contract.OriginalFilePath;
        else
            source = await ResolveOriginalFilePathAsync(contract, ct);

        if (string.IsNullOrWhiteSpace(source))
            return null;

        return approvalStamp.EnsurePristineBackupRelative(source);
    }

    private static string ResolveWorkFilePath(Contract contract, string pristinePath)
    {
        var work = contract.FilePath;
        if (string.IsNullOrWhiteSpace(work)
            || ContractApprovalStampService.IsSignedDocumentPath(work)
            || work.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            return pristinePath;

        return work;
    }

    /// <summary>
    /// برای هر مرحلهٔ تأییدشده: کلید امضا = فیلد امضای (Order-1) در قالب.
    /// </summary>
    private async Task<List<ContractSignatureSlot>> BuildSignatureSlotsAsync(
        List<ApprovalStepDto> steps,
        IReadOnlyList<string> signatureKeys,
        IReadOnlyList<string> keysInPristineDoc,
        CancellationToken ct)
    {
        var slots = new List<ContractSignatureSlot>();
        var docKeySet = new HashSet<string>(
            keysInPristineDoc.Select(ContractTemplateSystemFields.NormalizeKey),
            StringComparer.OrdinalIgnoreCase);

        foreach (var step in steps.Where(s => s.Status == "approved").OrderBy(s => s.Order))
        {
            var keyIndex = step.Order - 1;
            var placeholderKey = ResolvePlaceholderKeyForStep(
                keyIndex, step.Order, signatureKeys, keysInPristineDoc, docKeySet);

            var user = await db.Users
                .Include(u => u.UserPosition)
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == step.UserId, ct);
            if (user is null || string.IsNullOrWhiteSpace(user.SignatureImagePath))
                continue;

            var fullName = $"{user.FirstName} {user.LastName}".Trim();
            if (string.IsNullOrWhiteSpace(fullName))
                fullName = step.UserName?.Trim() ?? user.UserName?.Trim() ?? "";

            var sigFull = UserSignatureStorageService.ResolveFullPath(hostEnvironment, user.SignatureImagePath);
            if (!File.Exists(sigFull))
                continue;

            var ext = Path.GetExtension(sigFull);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";

            slots.Add(new ContractSignatureSlot(
                WorkflowOrder: step.Order,
                PlaceholderKey: placeholderKey,
                ImageBytes: await File.ReadAllBytesAsync(sigFull, ct),
                ImageExtension: ext,
                ApproverFullName: fullName,
                PositionTitle: user.UserPosition?.Name,
                WidthPx: UserSignatureDisplaySize.WidthPxFromDegree(user.SignatureDisplayDegree)));
        }

        return slots;
    }

    /// <summary>کلید فیلد امضای همان مرحله در طراح قالب (همان کلید باید در Word باشد: {{key}}).</summary>
    private static string ResolvePlaceholderKeyForStep(
        int keyIndex,
        int workflowOrder,
        IReadOnlyList<string> templateSignatureKeys,
        IReadOnlyList<string> _keysInPristineDoc,
        HashSet<string> _docKeySet)
    {
        if (keyIndex >= 0 && keyIndex < templateSignatureKeys.Count
            && !string.IsNullOrWhiteSpace(templateSignatureKeys[keyIndex]))
            return templateSignatureKeys[keyIndex].Trim();

        return $"sign_{workflowOrder}";
    }

    public sealed record ContractSignedFileResolution(
        string RelativePath,
        string? PdfRelativePath,
        string? DisplayFileName);

    /// <summary>آیا حداقل یک مرحله تأیید شده (فایل امضاشده قابل بازیابی است).</summary>
    public static bool HasSignedWorkflowDocument(Contract contract) =>
        DeserializeSteps(contract.StepsJson).Any(s => s.Status is "approved");

    /// <summary>مسیر فایل امضاشده — هنگام اصلاحیه از نسخهٔ قبل از اصلاح بازیابی می‌شود.</summary>
    public static async Task<ContractSignedFileResolution?> ResolveSignedFileForDownloadAsync(
        Contract contract,
        AppDbContext dbContext,
        CancellationToken ct = default)
    {
        var steps = DeserializeSteps(contract.StepsJson);
        if (!steps.Any(s => s.Status is "approved"))
            return null;

        var amendState = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        var amendActive = ContractAmendmentHelper.IsActive(amendState);
        var currentIsAmended = amendState?.AmendedVersionNumber is not null
            && contract.CurrentVersionNumber == amendState.AmendedVersionNumber;

        if (amendActive
            && !string.IsNullOrWhiteSpace(amendState!.SignedFilePath))
        {
            return new(
                amendState.SignedFilePath,
                amendState.SignedPdfFilePath,
                amendState.SignedFileName ?? contract.FileName);
        }

        static bool IsUsableSignedCurrent(string? path, bool amendedCurrent) =>
            !string.IsNullOrWhiteSpace(path)
            && !amendedCurrent
            && (ContractApprovalStampService.IsSignedDocumentPath(path)
                || path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".doc", StringComparison.OrdinalIgnoreCase));

        if (IsUsableSignedCurrent(contract.FilePath, amendActive && currentIsAmended))
            return new(contract.FilePath!, contract.PdfFilePath, contract.FileName);

        if (!amendActive && !string.IsNullOrWhiteSpace(contract.PdfFilePath))
            return new(contract.FilePath!, contract.PdfFilePath, contract.FileName);

        if (!amendActive && !string.IsNullOrWhiteSpace(contract.FilePath))
            return new(contract.FilePath, contract.PdfFilePath, contract.FileName);

        var priorVersion = await dbContext.ContractVersions.AsNoTracking()
            .Where(v => v.ContractId == contract.Id && !v.IsAmendedVersion)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        if (priorVersion is not null && !string.IsNullOrWhiteSpace(priorVersion.FilePath))
            return new(priorVersion.FilePath, priorVersion.PdfFilePath, priorVersion.FileName);

        var signedVersion = await dbContext.ContractVersions.AsNoTracking()
            .Where(v => v.ContractId == contract.Id
                        && v.FilePath != null
                        && v.FilePath.Contains("_signed_"))
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        if (signedVersion is not null)
            return new(signedVersion.FilePath, signedVersion.PdfFilePath, signedVersion.FileName);

        if (!string.IsNullOrWhiteSpace(contract.PdfFilePath) && !string.IsNullOrWhiteSpace(contract.FilePath))
            return new(contract.FilePath, contract.PdfFilePath, contract.FileName);

        return null;
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

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null) return false;

        var sender = !string.IsNullOrWhiteSpace(approverDisplayName) ? approverDisplayName : fallbackApproverName;
        if (string.IsNullOrWhiteSpace(sender)) sender = "سیستم";

        var code = await approvalLinks.CreateOrRefreshAsync(contract.Id, userId, ct);
        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var linkPath = string.IsNullOrWhiteSpace(publicBase)
            ? $"/approve/contract?c={code}"
            : $"{publicBase.TrimEnd('/')}/approve/contract?c={code}";

        var msg = isReminder
            ? await smsPatterns.RenderAsync("contract.approval.assignee.reminder", SmsPatternVars.Dict(
                ("contractNumber", contract.ContractNumber),
                ("linkPath", linkPath)
            ), ct)
            : await smsPatterns.RenderAsync("contract.approval.assignee.new", SmsPatternVars.Dict(
                ("contractNumber", contract.ContractNumber),
                ("sender", sender),
                ("linkPath", linkPath)
            ), ct);

        var inboxTitle = isReminder ? "یادآوری تأیید قرارداد" : "قرارداد برای تأیید";
        await inbox.SendToUserAsync(userId, inboxTitle, msg, ct);

        if (!smsSettings.ApprovalReferralSmsEnabled || string.IsNullOrWhiteSpace(user.PhoneNumber)) return false;
        var sent = await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
        if (sent && isReminder)
            await ApprovalReminderService.MarkReminderSentForContractAsync(db, contract.Id, userId, ct);
        return sent;
    }

    private async Task SendCreatorApprovalNotifySmsAsync(
        Contract contract,
        AppUser? approver,
        ApprovalStepDto? nextStep,
        CancellationToken ct)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();

        var creator = await db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == contract.CreatedByUserId, ct);
        if (creator is null) return;

        var approverLabel = FormatPersonLabel(approver, null);
        var subject = ResolveContractSubjectLabel(contract);

        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();

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

        var msg = await smsPatterns.RenderAsync("contract.creator.step.approved", SmsPatternVars.Dict(
            ("contractNumber", contract.ContractNumber),
            ("subject", subject),
            ("approverLabel", approverLabel),
            ("dateStr", dateStr),
            ("timeStr", timeStr),
            ("statusTail", statusTail)
        ), ct);

        await inbox.SendToUserAsync(contract.CreatedByUserId, "به‌روزرسانی قرارداد", msg, ct);
        if (!smsSettings.ContractCreatorApprovalNotifySmsEnabled || string.IsNullOrWhiteSpace(creator.PhoneNumber)) return;
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

        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();

        var msg = await smsPatterns.RenderAsync("contract.party.final.approved", SmsPatternVars.Dict(
            ("contractNumber", contract.ContractNumber),
            ("dateStr", dateStr),
            ("timeStr", timeStr)
        ), ct);
        await inbox.SendToMobileAsync(phone, "تأیید نهایی قرارداد", msg, ct);
        await smsSender.SendSmsAsync(new SmsRequest(phone, msg), ct);
    }

    private static string NormalizeDigits(string value)
        => value
            .Replace("۰", "0").Replace("۱", "1").Replace("۲", "2").Replace("۳", "3").Replace("۴", "4")
            .Replace("۵", "5").Replace("۶", "6").Replace("۷", "7").Replace("۸", "8").Replace("۹", "9")
            .Trim();

    public static List<ApprovalStepDto> DeserializeSteps(string? json) =>
        WorkflowStepJsonHelper.Deserialize(json);
}
