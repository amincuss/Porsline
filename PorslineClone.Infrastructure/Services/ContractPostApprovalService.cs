using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class ContractPostApprovalService(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ContractActionLinkService actionLinks,
    ISmsSender smsSender,
    IInboxMessageService inbox,
    IFrontendUrlResolver frontendUrls)
{
    public async Task TryStartPostApprovalAsync(Contract contract, CancellationToken ct = default)
    {
        if (contract.Status != ContractStatus.Approved) return;

        var existing = PostApprovalJsonHelper.DeserializeState(contract.PostApprovalJson);
        if (existing is { AssigneeUserIds.Count: > 0 }) return;

        ContractWorkflowTemplate? template = null;
        if (contract.WorkflowTemplateId is not null)
            template = await db.ContractWorkflowTemplates.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == contract.WorkflowTemplateId, ct);

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

        contract.PostApprovalJson = PostApprovalJsonHelper.SerializeState(state);
        await db.SaveChangesAsync(ct);

        var security = await SecuritySettingsHelper.GetAsync(db, ct);
        var linkExpiry = SecuritySettingsHelper.LinkExpiresAtUtc(security);
        var subject = ResolveSubject(contract);
        var publicBase = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);

        foreach (var userId in assigneeIds)
        {
            var code = await actionLinks.CreateOrRefreshAsync(contract.Id, userId, linkExpiry, ct);
            var actionPath = string.IsNullOrWhiteSpace(publicBase)
                ? $"/action/contract?c={code}"
                : $"{publicBase.TrimEnd('/')}/action/contract?c={code}";
            var adminPath = string.IsNullOrWhiteSpace(adminBase)
                ? "/admin/actions"
                : $"{adminBase.TrimEnd('/')}/admin/actions";

            var msg =
                $"قرارداد شماره «{contract.ContractNumber}» با موضوع «{subject}» جهت اقدام ({dirLabel}) برای شما ارسال شد.\n" +
                $"مشاهده گردش تأیید و ثبت وضعیت:\n{actionPath}\n" +
                $"یا از پنل: {adminPath}";

            await inbox.SendToUserAsync(userId, "اقدام قرارداد", msg, ct);

            var user = await userManager.FindByIdAsync(userId.ToString());
            var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
            if (smsSettings.ApprovalReferralSmsEnabled && !string.IsNullOrWhiteSpace(user?.PhoneNumber))
                await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
        }
    }

    public async Task<(bool Ok, string? Error)> UpdateStatusAsync(
        Guid contractId,
        Guid actorUserId,
        string status,
        string? note,
        CancellationToken ct = default)
    {
        var contract = await db.Contracts
            .Include(c => c.ContractType)
            .FirstOrDefaultAsync(x => x.Id == contractId, ct);
        if (contract is null) return (false, "قرارداد یافت نشد");

        var state = PostApprovalJsonHelper.DeserializeState(contract.PostApprovalJson);
        if (state is null || state.AssigneeUserIds.Count == 0)
            return (false, "فاز اقدام برای این قرارداد تعریف نشده است");

        if (!state.AssigneeUserIds.Contains(actorUserId))
            return (false, "شما در لیست اقدام‌کنندگان این قرارداد نیستید");

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
            contract.IsArchived = true;

        contract.PostApprovalJson = PostApprovalJsonHelper.SerializeState(state);
        await db.SaveChangesAsync(ct);

        if (normalized == "completed")
        {
            await NotifyApproversActionCompletedAsync(contract, state, actorName, ct);
            await NotifyCreatorActionCompletedAsync(contract, state, actorName, ct);
        }

        return (true, null);
    }

    private async Task NotifyApproversActionCompletedAsync(
        Contract contract,
        ContractPostApprovalStateDto state,
        string actorName,
        CancellationToken ct)
    {
        var steps = string.IsNullOrWhiteSpace(contract.StepsJson)
            ? []
            : JsonSerializer.Deserialize<List<ApprovalStepDto>>(contract.StepsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];

        var subject = ResolveSubject(contract);
        var body =
            $"اقدام قرارداد «{contract.ContractNumber}» ({subject}) با جهت «{state.ActionDirectionLabel}» توسط {actorName} به اتمام رسید.\n\n" +
            $"توضیحات اقدام‌کننده:\n{state.Note}";

        var approverIds = steps
            .Where(s => s.Status is "approved" or "rejected")
            .Select(s => s.UserId)
            .Distinct()
            .ToList();

        foreach (var uid in approverIds)
            await inbox.SendToUserAsync(uid, "اتمام اقدام قرارداد", body, ct);
    }

    private async Task NotifyCreatorActionCompletedAsync(
        Contract contract,
        ContractPostApprovalStateDto state,
        string actorName,
        CancellationToken ct)
    {
        if (contract.CreatedByUserId == Guid.Empty) return;

        var typeName = contract.ContractType?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(typeName))
        {
            typeName = await db.ContractTypes.AsNoTracking()
                .Where(t => t.Id == contract.ContractTypeId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(ct);
        }
        typeName = string.IsNullOrWhiteSpace(typeName) ? "—" : typeName.Trim();

        var subject = ResolveSubject(contract);
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);
        var viewPath = string.IsNullOrWhiteSpace(adminBase)
            ? $"/admin/contracts?id={contract.Id}"
            : $"{adminBase.TrimEnd('/')}/admin/contracts?id={contract.Id}";

        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var actorLabel = string.IsNullOrWhiteSpace(actorName) ? "اقدام‌کننده" : actorName.Trim();
        var noteBlock = string.IsNullOrWhiteSpace(state.Note)
            ? ""
            : $"\nتوضیحات اقدام:\n{state.Note.Trim()}";

        var body =
            "اتمام کار فاز اقدام قرارداد\n" +
            $"شماره قرارداد: {contract.ContractNumber}\n" +
            $"نوع قرارداد: {typeName}\n" +
            $"موضوع: {subject}\n" +
            $"جهت اقدام: {state.ActionDirectionLabel}\n" +
            $"اقدام‌کننده: {actorLabel}\n" +
            $"زمان ثبت: {dateStr} ساعت {timeStr}" +
            noteBlock +
            $"\n\nمشاهده پرونده:\n{viewPath}\n" +
            "پرونده به بایگانی منتقل شد.";

        await inbox.SendToUserAsync(contract.CreatedByUserId, "اتمام اقدام قرارداد", body, ct);

        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ContractActionCompletedCreatorSmsEnabled) return;

        var creator = await userManager.FindByIdAsync(contract.CreatedByUserId.ToString());
        if (creator is null || string.IsNullOrWhiteSpace(creator.PhoneNumber)) return;
        await smsSender.SendSmsAsync(new SmsRequest(creator.PhoneNumber, body), ct);
    }

    private static string ResolveSubject(Contract contract) =>
        !string.IsNullOrWhiteSpace(contract.Title)
            ? contract.Title
            : contract.SubjectPersonName;
}
