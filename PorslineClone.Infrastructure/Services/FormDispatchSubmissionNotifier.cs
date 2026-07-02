using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.SmsPatterns;

namespace PorslineClone.Infrastructure.Services;

/// <summary>اعلان به کارشناس ارسال‌کننده لینک فرم (پس از ثبت یا تأیید نهایی گردش).</summary>
public class FormDispatchSubmissionNotifier(
    AppDbContext db,
    IInboxMessageService inbox,
    ISmsSender smsSender,
    ISmsPatternService smsPatterns,
    IFrontendUrlResolver frontendUrls,
    FormSubmissionApprovalLinkService approvalLinks)
{
    public async Task NotifySenderAfterSubmitAsync(
        FormSubmission submission,
        Form form,
        FormDispatchLink link,
        Responder? responder,
        CancellationToken ct = default)
    {
        if (link.SentByUserId is not { } senderId || senderId == Guid.Empty)
            return;

        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.SurveyCompletedNotificationEnabled)
            return;

        var sender = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == senderId, ct);
        var honorificName = ResponderHonorific.FormatFullName(link.ResponderFullName, responder?.Gender);

        var viewUrl = await BuildUserFormsViewUrlAsync(submission.Id, ct);
        var workflowRunsUrl = await BuildWorkflowRunsUrlAsync(ct);

        var lines = new List<string>
        {
            $"پاسخگو {honorificName} فرم «{form.Title}» را تکمیل و ثبت کرد.",
        };

        if (!string.IsNullOrWhiteSpace(submission.TrackingCode))
        {
            lines.Add("");
            lines.Add($"کد پیگیری ثبت‌شده: {SmsDateTimeFormatter.ToPersianDigits(submission.TrackingCode)}");
        }

        lines.Add("");
        lines.Add("مشاهده پاسخ (لینک سریع):");
        lines.Add(viewUrl);

        switch (submission.Status)
        {
            case FormSubmissionStatus.InProgress:
                lines.Add("");
                lines.Add("گردش کار به‌صورت خودکار شروع شد.");
                lines.Add("پیگیری گردش:");
                lines.Add(workflowRunsUrl);
                break;
            case FormSubmissionStatus.Pending when submission.WorkflowTemplateId is not null:
                lines.Add("");
                lines.Add("گردش انتصاب شده؛ برای شروع گردش:");
                lines.Add($"{viewUrl}&action=start");
                break;
            case FormSubmissionStatus.Submitted:
                lines.Add("");
                lines.Add("برای انتصاب یا شروع گردش کار به لینک بالا مراجعه کنید.");
                break;
        }

        var body = string.Join('\n', lines);
        await inbox.SendToUserAsync(senderId, "ثبت فرم توسط پاسخگو", body, ct);

        if (sender is not null && !string.IsNullOrWhiteSpace(sender.PhoneNumber))
            await smsSender.SendSmsAsync(new SmsRequest(sender.PhoneNumber, body), ct);
    }

    /// <summary>پس از شروع گردش تأیید (توسط کارشناس یا خودکار) — پیامک اطلاع‌رسانی به پاسخگو.</summary>
    public async Task NotifyResponderWorkflowStartedAsync(
        FormSubmission submission,
        CancellationToken ct = default)
    {
        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.FormWorkflowStartedResponderSmsEnabled)
            return;

        FormDispatchLink? link = null;
        Responder? responder = null;
        if (submission.DispatchLinkId is Guid linkId)
        {
            link = await db.FormDispatchLinks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (link is not null)
                responder = await db.Responders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == link.ResponderId, ct);
        }

        var mobile = (link?.ResponderMobileNumber ?? submission.SubmitterEmail ?? "").Trim();
        if (string.IsNullOrWhiteSpace(mobile))
            return;

        var responderName = link?.ResponderFullName?.Trim();
        if (string.IsNullOrWhiteSpace(responderName))
            responderName = submission.SubmitterName?.Trim();
        var honorificName = ResponderHonorific.FormatFullName(responderName, responder?.Gender);

        var formTitle = submission.Form?.Title ?? await db.Forms.AsNoTracking()
            .Where(f => f.Id == submission.FormId)
            .Select(f => f.Title)
            .FirstOrDefaultAsync(ct) ?? "فرم";

        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var trackingPart = string.IsNullOrWhiteSpace(submission.TrackingCode)
            ? ""
            : $"\nشماره پیگیری: {submission.TrackingCode}";

        var body = await smsPatterns.RenderAsync("form.workflow.started.responder", SmsPatternVars.Dict(
            ("honorificName", honorificName),
            ("formTitle", formTitle),
            ("trackingPart", trackingPart),
            ("dateStr", dateStr),
            ("timeStr", timeStr)
        ), ct);

        await smsSender.SendSmsAsync(new SmsRequest(mobile, body), ct);
    }

    /// <summary>پس از ثبت فرم در وب — همان کد پیگیری صفحه موفقیت به موبایل ثبت‌نام‌کننده (پاسخگو).</summary>
    public async Task NotifyRegistrantTrackingCodeAsync(
        FormSubmission submission,
        Form form,
        FormDispatchLink? link,
        Responder? responder,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(submission.TrackingCode))
            return;

        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.FormSubmissionTrackingSmsEnabled)
            return;

        var mobile = FormSubmissionMobileHelper.ResolveRegistrantMobile(
            link?.ResponderMobileNumber,
            responder?.MobileNumber,
            submission.SubmitterEmail);
        if (string.IsNullOrWhiteSpace(mobile))
            return;

        var honorificName = ResponderHonorific.FormatFullName(
            link?.ResponderFullName ?? submission.SubmitterName,
            responder?.Gender);

        var trackingDisplay = SmsDateTimeFormatter.ToPersianDigits(submission.TrackingCode.Trim());

        var body = await smsPatterns.RenderAsync("form.submission.tracking.responder", SmsPatternVars.Dict(
            ("honorificName", honorificName),
            ("formTitle", form.Title),
            ("trackingDisplay", trackingDisplay)
        ), ct);

        await smsSender.SendSmsAsync(new SmsRequest(mobile, body), ct);
    }

    /// <summary>سازگاری با نام قبلی.</summary>
    public Task NotifyResponderTrackingCodeAsync(
        FormSubmission submission,
        Form form,
        FormDispatchLink link,
        Responder? responder,
        CancellationToken ct = default)
        => NotifyRegistrantTrackingCodeAsync(submission, form, link, responder, ct);

    public async Task NotifySenderAfterFullApprovalAsync(
        FormSubmission submission,
        CancellationToken ct = default)
    {
        if (submission.Status != FormSubmissionStatus.Approved)
            return;

        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.FormWorkflowCompletedSenderSmsEnabled)
            return;

        FormDispatchLink? link = null;
        Responder? responder = null;
        if (submission.DispatchLinkId is Guid linkId)
        {
            link = await db.FormDispatchLinks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (link is not null)
                responder = await db.Responders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == link.ResponderId, ct);
        }

        if (link?.SentByUserId is not { } senderId || senderId == Guid.Empty)
            return;

        var sender = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == senderId, ct);
        var formTitle = submission.Form?.Title ?? await db.Forms.AsNoTracking()
            .Where(f => f.Id == submission.FormId)
            .Select(f => f.Title)
            .FirstOrDefaultAsync(ct) ?? "فرم";

        var responderName = link.ResponderFullName;
        if (string.IsNullOrWhiteSpace(responderName))
            responderName = submission.SubmitterName;
        var honorificName = ResponderHonorific.FormatFullName(responderName, responder?.Gender);

        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var viewUrl = await BuildUserFormsViewUrlAsync(submission.Id, ct);

        var postState = PostApprovalJsonHelper.DeserializeState(submission.PostApprovalJson);
        var assigneeLine = "";
        if (postState is { AssigneeUserIds.Count: > 0 })
        {
            var assigneeNames = await FormatStaffHonorificsAsync(postState.AssigneeUserIds, ct);
            var direction = string.IsNullOrWhiteSpace(postState.ActionDirectionLabel)
                ? "اقدام"
                : postState.ActionDirectionLabel.Trim();
            assigneeLine =
                $"\nجهت {direction} به کارشناس مربوطه {assigneeNames} ارجاع شد.";
        }

        var body = await smsPatterns.RenderAsync("form.workflow.completed.sender", SmsPatternVars.Dict(
            ("formTitle", formTitle),
            ("honorificName", honorificName),
            ("dateStr", dateStr),
            ("timeStr", timeStr),
            ("assigneeLine", assigneeLine),
            ("viewUrl", viewUrl)
        ), ct);

        await inbox.SendToUserAsync(senderId, "تأیید نهایی فرم", body, ct);

        if (sender is not null && !string.IsNullOrWhiteSpace(sender.PhoneNumber))
            await smsSender.SendSmsAsync(new SmsRequest(sender.PhoneNumber, body), ct);
    }

    private async Task<string> BuildUserFormsViewUrlAsync(Guid submissionId, CancellationToken ct)
    {
        var adminBase = (await frontendUrls.ResolveAdminBaseUrlAsync(ct))?.TrimEnd('/') ?? "";
        return string.IsNullOrWhiteSpace(adminBase)
            ? $"/admin/responders/user-forms?id={submissionId}"
            : $"{adminBase}/admin/responders/user-forms?id={submissionId}";
    }

    private async Task<string> BuildWorkflowRunsUrlAsync(CancellationToken ct)
    {
        var adminBase = (await frontendUrls.ResolveAdminBaseUrlAsync(ct))?.TrimEnd('/') ?? "";
        return string.IsNullOrWhiteSpace(adminBase)
            ? "/admin/forms/workflow-runs"
            : $"{adminBase}/admin/forms/workflow-runs";
    }

    /// <summary>پس از «اتمام کار» فاز اقدام — پیامک به ارسال‌کننده لینک و پاسخگو.</summary>
    public async Task NotifyAfterActionPhaseCompletedAsync(
        FormSubmission submission,
        ContractPostApprovalStateDto state,
        string actorName,
        CancellationToken ct = default)
    {
        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        var formTitle = submission.Form?.Title ?? await db.Forms.AsNoTracking()
            .Where(f => f.Id == submission.FormId)
            .Select(f => f.Title)
            .FirstOrDefaultAsync(ct) ?? "فرم";

        FormDispatchLink? link = null;
        Responder? responder = null;
        if (submission.DispatchLinkId is Guid linkId)
        {
            link = await db.FormDispatchLinks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (link is not null)
                responder = await db.Responders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == link.ResponderId, ct);
        }

        var responderName = link?.ResponderFullName?.Trim();
        if (string.IsNullOrWhiteSpace(responderName))
            responderName = submission.SubmitterName?.Trim();
        var honorificName = ResponderHonorific.FormatFullName(responderName, responder?.Gender);

        var mobile = (link?.ResponderMobileNumber ?? submission.SubmitterEmail ?? "").Trim();
        var mobileDisplay = string.IsNullOrWhiteSpace(mobile)
            ? "—"
            : SmsDateTimeFormatter.ToPersianDigits(mobile);

        var nationalCode = (responder?.NationalCode ?? "").Trim();
        var nationalCodeDisplay = string.IsNullOrWhiteSpace(nationalCode)
            ? "—"
            : SmsDateTimeFormatter.ToPersianDigits(nationalCode);

        var completedAt = state.CompletedAtUtc ?? DateTime.UtcNow;
        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcTehran(completedAt);
        var actorLabel = string.IsNullOrWhiteSpace(actorName) ? "اقدام‌کننده" : actorName.Trim();
        var directionLabel = string.IsNullOrWhiteSpace(state.ActionDirectionLabel) ? "جهت اقدام" : state.ActionDirectionLabel.Trim();
        var noteBlock = string.IsNullOrWhiteSpace(state.Note)
            ? ""
            : $"\n\nتوضیحات اقدام‌کننده:\n{state.Note.Trim()}";

        if (smsSettings.FormActionPhaseCompletedSenderSmsEnabled
            && link?.SentByUserId is Guid senderId
            && senderId != Guid.Empty)
        {
            var sender = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == senderId, ct);
            var viewUrl = await BuildUserFormsViewUrlAsync(submission.Id, ct);

            var senderBody = await smsPatterns.RenderAsync("form.action.completed.sender", SmsPatternVars.Dict(
                ("formTitle", formTitle),
                ("honorificName", honorificName),
                ("dateStr", dateStr),
                ("timeStr", timeStr),
                ("directionLabel", directionLabel),
                ("actorLabel", actorLabel),
                ("noteBlock", noteBlock),
                ("viewUrl", viewUrl)
            ), ct);

            await inbox.SendToUserAsync(senderId, "اتمام اقدام فرم", senderBody, ct);

            if (sender is not null && !string.IsNullOrWhiteSpace(sender.PhoneNumber))
                await smsSender.SendSmsAsync(new SmsRequest(sender.PhoneNumber, senderBody), ct);
        }

        if (smsSettings.FormResponderApprovedSmsEnabled && !string.IsNullOrWhiteSpace(mobile))
        {
            var responderBody = await BuildResponderWorkflowStatusSmsAsync(
                honorificName,
                formTitle,
                "در مرحله «انجام شده» قرار گرفت",
                $"تاریخ: {dateStr} — ساعت {timeStr}",
                ct);

            await smsSender.SendSmsAsync(new SmsRequest(mobile, responderBody), ct);
        }
    }

    /// <summary>پس از اتمام گردش توسط ارسال‌کننده (بایگانی پس از رد) — پیامک به پاسخگو.</summary>
    public async Task NotifyResponderAfterWorkflowClosedAsync(
        FormSubmission submission,
        CancellationToken ct = default)
    {
        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.FormWorkflowRejectedResponderSmsEnabled)
            return;

        var formTitle = await ResolveFormTitleAsync(submission, ct);
        var (honorificName, _, mobile) = await ResolveResponderContextAsync(submission, ct);
        if (string.IsNullOrWhiteSpace(mobile))
            return;

        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var responderBody = await BuildResponderWorkflowStatusSmsAsync(
            honorificName,
            formTitle,
            "به‌طور کامل رد شد",
            $"تاریخ: {dateStr} — ساعت {timeStr}",
            ct);

        await smsSender.SendSmsAsync(new SmsRequest(mobile, responderBody), ct);
    }

    /// <summary>پس از رد — پیامک فوری به ارسال‌کننده (درخواست مجدد / اتمام گردش) و به ردکننده</summary>
    public async Task NotifyAfterRejectAwaitingSenderActionAsync(
        FormSubmission submission,
        ApprovalStepDto rejectedStep,
        string? rejecterName,
        string? comment,
        CancellationToken ct = default)
    {
        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        var formTitle = await ResolveFormTitleAsync(submission, ct);
        var (honorificName, link, mobile) = await ResolveResponderContextAsync(submission, ct);

        var rejecterLabel = string.IsNullOrWhiteSpace(rejecterName)
            ? (rejectedStep.UserName ?? "تأییدکننده")
            : rejecterName.Trim();
        var commentBlock = string.IsNullOrWhiteSpace(comment)
            ? ""
            : $"\nیادداشت: {comment.Trim()}";
        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var actionUrl = await BuildUserFormsViewUrlAsync(submission.Id, ct);

        if (smsSettings.FormWorkflowRejectedSenderSmsEnabled
            && link?.SentByUserId is Guid senderId
            && senderId != Guid.Empty)
        {
            var sender = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == senderId, ct);
            var senderBody = await smsPatterns.RenderAsync("form.workflow.reject.awaiting.sender", SmsPatternVars.Dict(
                ("formTitle", formTitle),
                ("honorificName", honorificName),
                ("rejecterLabel", rejecterLabel),
                ("dateStr", dateStr),
                ("timeStr", timeStr),
                ("commentBlock", commentBlock),
                ("actionUrl", actionUrl)
            ), ct);

            await inbox.SendToUserAsync(senderId, "رد فرم — اقدام شما", senderBody, ct);
            if (sender is not null && !string.IsNullOrWhiteSpace(sender.PhoneNumber))
                await smsSender.SendSmsAsync(new SmsRequest(sender.PhoneNumber, senderBody), ct);
        }

        if (smsSettings.ApprovalReferralSmsEnabled)
        {
            var rejecter = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == rejectedStep.UserId, ct);
            if (rejecter is not null && !string.IsNullOrWhiteSpace(rejecter.PhoneNumber))
            {
                var rejecterBody = await smsPatterns.RenderAsync("form.workflow.reject.rejecter.confirm", SmsPatternVars.Dict(
                    ("formTitle", formTitle),
                    ("honorificName", honorificName),
                    ("commentBlock", commentBlock)
                ), ct);

                await inbox.SendToUserAsync(rejectedStep.UserId, "رد ثبت شد", rejecterBody, ct);
                await smsSender.SendSmsAsync(new SmsRequest(rejecter.PhoneNumber, rejecterBody), ct);
            }
        }

        if (smsSettings.FormWorkflowRejectedResponderSmsEnabled && !string.IsNullOrWhiteSpace(mobile))
        {
            var responderBody = await smsPatterns.RenderAsync("form.workflow.reject.responder", SmsPatternVars.Dict(
                ("honorificName", honorificName),
                ("formTitle", formTitle),
                ("rejecterLabel", rejecterLabel),
                ("commentBlock", commentBlock)
            ), ct);
            await smsSender.SendSmsAsync(new SmsRequest(mobile, responderBody), ct);
        }
    }

    /// <summary>پس از درخواست مجدد تأیید — پیامک فوری به ردکننده</summary>
    public async Task NotifyRejecterUrgentReapprovalAsync(
        FormSubmission submission,
        ApprovalStepDto rejecterStep,
        string? rejecterDisplayName,
        CancellationToken ct = default)
    {
        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.ApprovalReferralSmsEnabled) return;

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == rejecterStep.UserId, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber)) return;

        var formTitle = await ResolveFormTitleAsync(submission, ct);
        var (honorificName, _, _) = await ResolveResponderContextAsync(submission, ct);
        var code = await approvalLinks.CreateOrRefreshAsync(submission.Id, rejecterStep.UserId, ct);
        var publicBase = (await frontendUrls.ResolvePublicBaseUrlAsync(ct))?.TrimEnd('/') ?? "";
        var approvePath = string.IsNullOrWhiteSpace(publicBase)
            ? $"/approve/form?c={code}"
            : $"{publicBase}/approve/form?c={code}";

        var name = string.IsNullOrWhiteSpace(rejecterDisplayName)
            ? $"{user.FirstName} {user.LastName}".Trim()
            : rejecterDisplayName;
        if (string.IsNullOrWhiteSpace(name)) name = user.UserName ?? "تأییدکننده";

        var msg = await smsPatterns.RenderAsync("form.workflow.reject.urgent.reapprove", SmsPatternVars.Dict(
            ("formTitle", formTitle),
            ("honorificName", honorificName),
            ("approvePath", approvePath)
        ), ct);

        await inbox.SendToUserAsync(rejecterStep.UserId, "تأیید مجدد فوری", msg, ct);
        await smsSender.SendSmsAsync(new SmsRequest(user.PhoneNumber, msg), ct);
    }

    private async Task<string> ResolveFormTitleAsync(FormSubmission submission, CancellationToken ct) =>
        submission.Form?.Title ?? await db.Forms.AsNoTracking()
            .Where(f => f.Id == submission.FormId)
            .Select(f => f.Title)
            .FirstOrDefaultAsync(ct) ?? "فرم";

    private async Task<(string HonorificName, FormDispatchLink? Link, string Mobile)> ResolveResponderContextAsync(
        FormSubmission submission,
        CancellationToken ct)
    {
        FormDispatchLink? link = null;
        Responder? responder = null;
        if (submission.DispatchLinkId is Guid linkId)
        {
            link = await db.FormDispatchLinks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (link is not null)
                responder = await db.Responders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == link.ResponderId, ct);
        }

        var responderName = link?.ResponderFullName?.Trim();
        if (string.IsNullOrWhiteSpace(responderName))
            responderName = submission.SubmitterName?.Trim();
        var honorificName = ResponderHonorific.FormatFullName(responderName, responder?.Gender);
        var mobile = (link?.ResponderMobileNumber ?? submission.SubmitterEmail ?? "").Trim();
        return (honorificName, link, mobile);
    }

    /// <summary>پس از اتمام دستی گردش توسط ارسال‌کننده — بایگانی</summary>
    public async Task NotifyAfterWorkflowEndedBySenderAsync(
        FormSubmission submission,
        CancellationToken ct = default)
    {
        if (!submission.IsArchived) return;
        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        if (!smsSettings.FormWorkflowRejectedSenderSmsEnabled) return;

        var (_, link, _) = await ResolveResponderContextAsync(submission, ct);
        if (link?.SentByUserId is not Guid senderId || senderId == Guid.Empty) return;

        var formTitle = await ResolveFormTitleAsync(submission, ct);
        var archiveUrl = await BuildFormsArchiveUrlAsync(ct);
        var body =
            "اتمام گردش فرم\n" +
            $"پرونده «{formTitle}» توسط شما بایگانی شد.\n" +
            archiveUrl;

        await inbox.SendToUserAsync(senderId, "اتمام گردش", body, ct);
    }

    /// <summary>پس از رد قطعی گردش — پیامک به ارسال‌کننده لینک و پاسخگو؛ پرونده بایگانی شده است.</summary>
    public async Task NotifyAfterWorkflowRejectedAsync(
        FormSubmission submission,
        ApprovalStepDto rejectedStep,
        string? rejecterName,
        string? comment,
        CancellationToken ct = default)
    {
        if (submission.Status != FormSubmissionStatus.Rejected)
            return;

        var smsSettings = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(ct) ?? new SmsSettings();
        var formTitle = submission.Form?.Title ?? await db.Forms.AsNoTracking()
            .Where(f => f.Id == submission.FormId)
            .Select(f => f.Title)
            .FirstOrDefaultAsync(ct) ?? "فرم";

        FormDispatchLink? link = null;
        Responder? responder = null;
        if (submission.DispatchLinkId is Guid linkId)
        {
            link = await db.FormDispatchLinks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (link is not null)
                responder = await db.Responders.AsNoTracking().FirstOrDefaultAsync(x => x.Id == link.ResponderId, ct);
        }

        var responderName = link?.ResponderFullName?.Trim();
        if (string.IsNullOrWhiteSpace(responderName))
            responderName = submission.SubmitterName?.Trim();
        var honorificName = ResponderHonorific.FormatFullName(responderName, responder?.Gender);

        var mobile = (link?.ResponderMobileNumber ?? submission.SubmitterEmail ?? "").Trim();
        var rejecterLabel = string.IsNullOrWhiteSpace(rejecterName)
            ? (rejectedStep.UserName ?? "تأییدکننده")
            : rejecterName.Trim();
        var commentBlock = string.IsNullOrWhiteSpace(comment)
            ? ""
            : $"\n\nیادداشت: {comment.Trim()}";
        var (dateStr, timeStr) = SmsDateTimeFormatter.FormatUtcNowTehran();
        var viewUrl = await BuildUserFormsViewUrlAsync(submission.Id, ct);
        var archiveUrl = await BuildFormsArchiveUrlAsync(ct);

        if (smsSettings.FormWorkflowRejectedSenderSmsEnabled
            && link?.SentByUserId is Guid senderId
            && senderId != Guid.Empty)
        {
            var sender = await db.Users.AsNoTracking().FirstOrDefaultAsync(x => x.Id == senderId, ct);
            var senderBody = await smsPatterns.RenderAsync("form.workflow.rejected.final.sender", SmsPatternVars.Dict(
                ("formTitle", formTitle),
                ("honorificName", honorificName),
                ("dateStr", dateStr),
                ("timeStr", timeStr),
                ("rejecterLabel", rejecterLabel),
                ("stepOrder", rejectedStep.Order.ToString()),
                ("commentBlock", commentBlock),
                ("viewUrl", viewUrl),
                ("archiveUrl", archiveUrl)
            ), ct);

            await inbox.SendToUserAsync(senderId, "رد قطعی فرم", senderBody, ct);

            if (sender is not null && !string.IsNullOrWhiteSpace(sender.PhoneNumber))
                await smsSender.SendSmsAsync(new SmsRequest(sender.PhoneNumber, senderBody), ct);
        }

        if (smsSettings.FormWorkflowRejectedResponderSmsEnabled && !string.IsNullOrWhiteSpace(mobile))
        {
            var responderBody = await BuildResponderWorkflowStatusSmsAsync(
                honorificName,
                formTitle,
                "به‌طور کامل رد شد",
                $"تاریخ: {dateStr} — ساعت {timeStr}{commentBlock}",
                ct);

            await smsSender.SendSmsAsync(new SmsRequest(mobile, responderBody), ct);
        }
    }

    private async Task<string> BuildResponderWorkflowStatusSmsAsync(
        string honorificName,
        string formTitle,
        string statusPhrase,
        string? extraLines,
        CancellationToken ct)
    {
        var name = string.IsNullOrWhiteSpace(honorificName) ? "کاربر" : honorificName.Trim();
        return await smsPatterns.RenderAsync("form.responder.workflow.status", SmsPatternVars.Dict(
            ("honorificName", name),
            ("statusPhrase", statusPhrase),
            ("formTitle", formTitle),
            ("extraLines", string.IsNullOrWhiteSpace(extraLines) ? "" : extraLines.Trim())
        ), ct);
    }

    private async Task<string> FormatStaffHonorificsAsync(IReadOnlyList<Guid> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0) return "کارشناس";
        var users = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.FirstName, u.LastName, u.Gender })
            .ToListAsync(ct);
        if (users.Count == 0) return "کارشناس";
        return string.Join(
            " و ",
            users.Select(u =>
                ResponderHonorific.FormatFullName($"{u.FirstName} {u.LastName}".Trim(), u.Gender)));
    }

    private async Task<string> BuildFormsArchiveUrlAsync(CancellationToken ct)
    {
        var adminBase = (await frontendUrls.ResolveAdminBaseUrlAsync(ct))?.TrimEnd('/') ?? "";
        return string.IsNullOrWhiteSpace(adminBase)
            ? "/admin/forms/archive"
            : $"{adminBase}/admin/forms/archive";
    }
}
