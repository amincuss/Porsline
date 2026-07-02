using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.Sms;
using PorslineClone.Infrastructure.Services.SmsPatterns;

namespace PorslineClone.Infrastructure.Services.FormDispatch;

public record FormDispatchGroupSendStatusDto(
    Guid JobId,
    string Status,
    int TotalCount,
    int ProcessedCount,
    int SentCount,
    int FailedCount,
    string? ErrorMessage);

public record IncompleteDispatchTarget(Guid ResponderId, string FullName, string MobileNumber, Guid FormId);

public record GroupFormSendPreviewDto(
    Guid FormId,
    string FormTitle,
    int TotalMembers,
    int EligibleCount,
    int SkippedRegisteredCount);

public class FormDispatchGroupSendService(
    AppDbContext db,
    ISmsSender smsSender,
    ISmsPatternService smsPatterns,
    IFrontendUrlResolver frontendUrls)
{
    public async Task<(int Sent, int Failed)> DispatchToRespondersAsync(
        Form form,
        IReadOnlyList<(Guid Id, string FullName, string MobileNumber)> responders,
        FormWorkflowTemplate? workflowTemplate,
        string? smsMessageMode,
        string? customSmsBody,
        Guid? sentByUserId,
        CancellationToken ct,
        string smsSource = "form.dispatch")
    {
        await smsPatterns.EnsureSeededAsync(ct);

        var baseUrl = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("آدرس پایهٔ عمومی در تنظیمات سایت تعریف نشده است");

        var sent = 0;
        var failed = 0;
        var security = await db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var defaultExpiry = SecuritySettingsHelper.LinkExpiresAtUtc(security ?? new SecuritySettings());
        var linkExpiry = form.ExpiresAtUtc.HasValue && form.ExpiresAtUtc.Value < defaultExpiry
            ? form.ExpiresAtUtc.Value
            : defaultExpiry;

        for (var i = 0; i < responders.Count; i++)
        {
            var ok = await DispatchOneAsync(
                form, responders[i], workflowTemplate, smsMessageMode, customSmsBody, sentByUserId, baseUrl, linkExpiry, smsSource, ct);
            if (ok) sent++; else failed++;
        }

        return (sent, failed);
    }

    public async Task<FormDispatchGroupSendJob> CreateGroupJobAsync(
        Form form,
        Guid groupId,
        FormWorkflowTemplate? workflowTemplate,
        string? smsMessageMode,
        string? customSmsBody,
        Guid? createdByUserId,
        CancellationToken ct)
    {
        var responders = await db.ResponderGroupMembers
            .Where(x => x.GroupId == groupId && !x.Responder.IsDeleted)
            .Select(x => new { x.Responder.Id })
            .Distinct()
            .ToListAsync(ct);

        if (responders.Count == 0)
            throw new InvalidOperationException("هیچ پاسخگویی برای ارسال یافت نشد");

        var job = new FormDispatchGroupSendJob
        {
            Id = Guid.NewGuid(),
            FormId = form.Id,
            GroupId = groupId,
            WorkflowTemplateId = workflowTemplate?.Id,
            SkipWorkflow = workflowTemplate is null,
            SmsMessageMode = smsMessageMode,
            CustomSmsBody = customSmsBody,
            Status = FormDispatchGroupSendJobStatus.Queued,
            TotalCount = responders.Count,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.FormDispatchGroupSendJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }

    public async Task<FormDispatchGroupSendJob> CreateIncompleteGroupJobAsync(
        Guid groupId,
        Guid? filterFormId,
        Guid? createdByUserId,
        CancellationToken ct)
    {
        var targets = await GetIncompleteTargetsAsync(groupId, filterFormId, ct);
        if (targets.Count == 0)
            throw new InvalidOperationException("همه اعضای گروه قبلاً این فرم را ثبت کرده‌اند؛ پیامکی ارسال نمی‌شود");

        var jobFormId = filterFormId is Guid ff && ff != Guid.Empty ? ff : Guid.Empty;

        var job = new FormDispatchGroupSendJob
        {
            Id = Guid.NewGuid(),
            FormId = jobFormId,
            GroupId = groupId,
            WorkflowTemplateId = null,
            SkipWorkflow = true,
            OnlyIncompleteSubmissions = true,
            Status = FormDispatchGroupSendJobStatus.Queued,
            TotalCount = targets.Count,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.FormDispatchGroupSendJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }

    public async Task<GroupFormSendPreviewDto?> GetSendPreviewAsync(
        Guid groupId,
        Guid formId,
        CancellationToken ct = default)
    {
        if (formId == Guid.Empty) return null;

        var groupExists = await db.ResponderGroups.AsNoTracking()
            .AnyAsync(x => x.Id == groupId && !x.IsDeleted, ct);
        if (!groupExists) return null;

        var form = await db.Forms.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == formId && !f.IsDeleted, ct);
        if (form is null) return null;

        var memberRows = await (
            from m in db.ResponderGroupMembers.AsNoTracking()
            where m.GroupId == groupId
            join r in db.Responders.AsNoTracking() on m.ResponderId equals r.Id
            select new { r.Id, r.FullName, r.MobileNumber }
        ).ToListAsync(ct);

        var members = memberRows
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .ToList();

        var registeredSet = (await (
            from m in db.ResponderGroupMembers.AsNoTracking()
            where m.GroupId == groupId
            join s in db.FormSubmissions.AsNoTracking() on m.ResponderId equals s.ResponderId
            where s.FormId == formId
            select m.ResponderId
        ).Distinct().ToListAsync(ct)).ToHashSet();

        var eligible = members.Count(m => !registeredSet.Contains(m.Id));
        return new GroupFormSendPreviewDto(
            formId,
            form.Title,
            members.Count,
            eligible,
            members.Count - eligible);
    }

    private async Task<List<IncompleteDispatchTarget>> GetIncompleteTargetsAsync(
        Guid groupId,
        Guid? filterFormId,
        CancellationToken ct)
    {
        if (filterFormId is not Guid formId || formId == Guid.Empty)
            throw new InvalidOperationException("فرم را انتخاب کنید");

        var preview = await GetSendPreviewAsync(groupId, formId, ct)
            ?? throw new InvalidOperationException("گروه یا فرم یافت نشد");

        if (preview.EligibleCount <= 0)
            return [];

        var memberRows = await (
            from m in db.ResponderGroupMembers.AsNoTracking()
            where m.GroupId == groupId
            join r in db.Responders.AsNoTracking() on m.ResponderId equals r.Id
            select new { r.Id, r.FullName, r.MobileNumber }
        ).ToListAsync(ct);

        var registeredSet = (await (
            from m in db.ResponderGroupMembers.AsNoTracking()
            where m.GroupId == groupId
            join s in db.FormSubmissions.AsNoTracking() on m.ResponderId equals s.ResponderId
            where s.FormId == formId
            select m.ResponderId
        ).Distinct().ToListAsync(ct)).ToHashSet();

        return memberRows
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .Where(m => !registeredSet.Contains(m.Id))
            .Select(m => new IncompleteDispatchTarget(
                m.Id,
                m.FullName ?? "",
                m.MobileNumber ?? "",
                formId))
            .OrderBy(m => m.FullName)
            .ToList();
    }

    public async Task SetHangfireJobIdAsync(Guid jobId, string hangfireJobId, CancellationToken ct = default)
    {
        var job = await db.FormDispatchGroupSendJobs.FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new InvalidOperationException("کار یافت نشد");
        job.HangfireJobId = hangfireJobId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<FormDispatchGroupSendStatusDto?> GetStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.FormDispatchGroupSendJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == jobId, ct);
        return job is null ? null : MapStatus(job);
    }

    public async Task<FormDispatchGroupSendStatusDto?> GetActiveJobForUserAsync(
        Guid? userId,
        CancellationToken ct = default)
    {
        if (userId is not Guid uid || uid == Guid.Empty)
            return null;

        var job = await db.FormDispatchGroupSendJobs.AsNoTracking()
            .Where(x => x.CreatedByUserId == uid)
            .Where(x => x.Status == FormDispatchGroupSendJobStatus.Queued
                || x.Status == FormDispatchGroupSendJobStatus.Running)
            .OrderByDescending(x => x.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return job is null ? null : MapStatus(job);
    }

    public async Task<(bool Cancelled, string? HangfireJobId)> CancelJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.FormDispatchGroupSendJobs.FirstOrDefaultAsync(x => x.Id == jobId, ct);
        if (job is null) return (false, null);
        if (job.Status is FormDispatchGroupSendJobStatus.Completed
            or FormDispatchGroupSendJobStatus.Failed
            or FormDispatchGroupSendJobStatus.Cancelled)
            return (false, job.HangfireJobId);

        job.Status = FormDispatchGroupSendJobStatus.Cancelled;
        job.ErrorMessage = "لغو شده توسط کاربر";
        job.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (true, job.HangfireJobId);
    }

    public async Task ExecuteGroupJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.FormDispatchGroupSendJobs.FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new InvalidOperationException("کار یافت نشد");

        if (job.Status == FormDispatchGroupSendJobStatus.Cancelled)
            return;

        job.Status = FormDispatchGroupSendJobStatus.Running;
        job.ProcessedCount = 0;
        job.SentCount = 0;
        job.FailedCount = 0;
        job.ErrorMessage = null;
        await db.SaveChangesAsync(ct);

        try
        {
            if (job.OnlyIncompleteSubmissions)
            {
                await ExecuteIncompleteGroupJobAsync(job, ct);
                return;
            }

            await ExecuteStandardGroupJobAsync(job, ct);
        }
        catch (Exception ex)
        {
            job.Status = FormDispatchGroupSendJobStatus.Failed;
            job.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ExecuteIncompleteGroupJobAsync(FormDispatchGroupSendJob job, CancellationToken ct)
    {
        var filterFormId = job.FormId == Guid.Empty ? (Guid?)null : job.FormId;
        var targets = await GetIncompleteTargetsAsync(job.GroupId, filterFormId, ct);
        job.TotalCount = targets.Count;
        await db.SaveChangesAsync(ct);

        if (targets.Count == 0)
        {
            job.Status = FormDispatchGroupSendJobStatus.Completed;
            job.CompletedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return;
        }

        var baseUrl = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("آدرس پایهٔ عمومی در تنظیمات سایت تعریف نشده است");

        var security = await db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var defaultExpiry = SecuritySettingsHelper.LinkExpiresAtUtc(security ?? new SecuritySettings());

        for (var index = 0; index < targets.Count; index++)
        {
            if (await IsJobCancelledAsync(job.Id, ct))
            {
                await MarkJobCancelledAsync(job, ct);
                return;
            }

            var target = targets[index];
            var targetForm = await db.Forms.FirstOrDefaultAsync(x => x.Id == target.FormId && !x.IsDeleted, ct);
            if (targetForm is null || !targetForm.IsActive)
            {
                job.ProcessedCount++;
                job.FailedCount++;
                await db.SaveChangesAsync(ct);
                continue;
            }

            var linkExpiry = targetForm.ExpiresAtUtc.HasValue && targetForm.ExpiresAtUtc.Value < defaultExpiry
                ? targetForm.ExpiresAtUtc.Value
                : defaultExpiry;

            var ok = await DispatchOneAsync(
                targetForm,
                (target.ResponderId, target.FullName, target.MobileNumber),
                workflowTemplate: null,
                job.SmsMessageMode,
                job.CustomSmsBody,
                job.CreatedByUserId,
                baseUrl,
                linkExpiry,
                "form.dispatch.incomplete",
                ct);

            job.ProcessedCount++;
            if (ok) job.SentCount++; else job.FailedCount++;
            await db.SaveChangesAsync(ct);
        }

        job.Status = FormDispatchGroupSendJobStatus.Completed;
        job.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private async Task ExecuteStandardGroupJobAsync(FormDispatchGroupSendJob job, CancellationToken ct)
    {
        var form = await db.Forms.FirstOrDefaultAsync(x => x.Id == job.FormId && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("فرم یافت نشد");

        FormWorkflowTemplate? workflowTemplate = null;
        if (job.WorkflowTemplateId is Guid wtId)
        {
            workflowTemplate = await db.FormWorkflowTemplates
                .FirstOrDefaultAsync(x => x.Id == wtId && x.IsActive, ct);
        }

        var baseUrl = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("آدرس پایهٔ عمومی در تنظیمات سایت تعریف نشده است");

        var security = await db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(ct);
        var defaultExpiry = SecuritySettingsHelper.LinkExpiresAtUtc(security ?? new SecuritySettings());
        var linkExpiry = form.ExpiresAtUtc.HasValue && form.ExpiresAtUtc.Value < defaultExpiry
            ? form.ExpiresAtUtc.Value
            : defaultExpiry;

        var members = await LoadActiveGroupMembersAsync(job.GroupId, ct);

        job.TotalCount = members.Count;
        await db.SaveChangesAsync(ct);

        for (var index = 0; index < members.Count; index++)
        {
            if (await IsJobCancelledAsync(job.Id, ct))
            {
                await MarkJobCancelledAsync(job, ct);
                return;
            }

            var member = members[index];
            var ok = await DispatchOneAsync(
                form,
                (member.ResponderId, member.FullName, member.MobileNumber),
                workflowTemplate,
                job.SmsMessageMode,
                job.CustomSmsBody,
                job.CreatedByUserId,
                baseUrl,
                linkExpiry,
                "form.dispatch.group",
                ct);

            job.ProcessedCount++;
            if (ok) job.SentCount++; else job.FailedCount++;
            await db.SaveChangesAsync(ct);
        }

        job.Status = FormDispatchGroupSendJobStatus.Completed;
        job.CompletedAtUtc = DateTime.UtcNow;
    }

    private async Task<bool> DispatchOneAsync(
        Form form,
        (Guid Id, string FullName, string MobileNumber) responder,
        FormWorkflowTemplate? workflowTemplate,
        string? smsMessageMode,
        string? customSmsBody,
        Guid? sentByUserId,
        string baseUrl,
        DateTime linkExpiry,
        string smsSource,
        CancellationToken ct)
    {
        var mobile = await ResolveDispatchMobileAsync(responder.Id, responder.MobileNumber, ct);
        if (!FormSubmissionMobileHelper.IsValidMobile(mobile))
            return false;

        var code = await GenerateUniqueCodeAsync(ct);
        db.FormDispatchLinks.Add(new FormDispatchLink
        {
            Id = Guid.NewGuid(),
            FormId = form.Id,
            ResponderId = responder.Id,
            ResponderMobileNumber = mobile,
            ResponderFullName = responder.FullName,
            Code = code,
            ExpiresAtUtc = linkExpiry,
            WorkflowTemplateId = workflowTemplate?.Id,
            SentByUserId = sentByUserId,
        });
        await db.SaveChangesAsync(ct);

        var link = $"{baseUrl.TrimEnd('/')}/forms/fill?c={code}";
        var msg = await BuildDispatchSmsMessageAsync(
            responder.FullName, form, link, smsMessageMode, customSmsBody, ct);
        if (string.IsNullOrWhiteSpace(msg))
            return false;

        return await SendDispatchSmsAsync(mobile, msg, smsSource, ct);
    }

    /// <summary>ارسال بدون throttle — برای ارسال گروهی پشت‌سرهم.</summary>
    private async Task<bool> SendDispatchSmsAsync(string mobile, string msg, string smsSource, CancellationToken ct)
    {
        var req = new SmsRequest(mobile, msg, smsSource, SkipThrottle: true);
        var ok = await smsSender.SendSmsAsync(req, ct);
        if (ok) return true;
        return await smsSender.SendSmsAsync(req, ct);
    }

    private async Task<string> ResolveDispatchMobileAsync(
        Guid responderId,
        string entityMobile,
        CancellationToken ct)
    {
        var latestLinkMobile = await db.FormDispatchLinks.AsNoTracking()
            .Where(l => l.ResponderId == responderId && l.ResponderMobileNumber != null && l.ResponderMobileNumber != "")
            .OrderByDescending(l => l.CreatedAtUtc)
            .Select(l => l.ResponderMobileNumber)
            .FirstOrDefaultAsync(ct);

        return FormSubmissionMobileHelper.ResolveRegistrantMobile(latestLinkMobile, entityMobile, null);
    }

    private async Task<string> BuildDispatchSmsMessageAsync(
        string fullName,
        Form form,
        string link,
        string? smsMessageMode,
        string? customSmsBody,
        CancellationToken ct)
    {
        var (firstName, lastName, full) = ResponderNameHelper.SplitFullName(fullName);
        var dispatchVars = SmsPatternVars.Dict(
            ("firstName", firstName),
            ("lastName", lastName),
            ("fullName", full),
            ("formTitle", form.Title),
            ("link", link));

        if (string.Equals(smsMessageMode, "manual", StringComparison.OrdinalIgnoreCase))
        {
            var body = ApplyDispatchPlaceholders((customSmsBody ?? "").Trim(), firstName, lastName, full, form.Title);
            return await smsPatterns.RenderAsync("form.dispatch.link.manual", SmsPatternVars.Dict(
                ("customSmsBody", body),
                ("firstName", firstName),
                ("lastName", lastName),
                ("fullName", full),
                ("formTitle", form.Title),
                ("link", link)
            ), ct);
        }

        return await smsPatterns.RenderAsync("form.dispatch.link.default", dispatchVars, ct);
    }

    private static string ApplyDispatchPlaceholders(
        string text,
        string firstName,
        string lastName,
        string fullName,
        string formTitle)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return text
            .Replace("{firstName}", firstName, StringComparison.Ordinal)
            .Replace("{lastName}", lastName, StringComparison.Ordinal)
            .Replace("{fullName}", fullName, StringComparison.Ordinal)
            .Replace("{formTitle}", formTitle, StringComparison.Ordinal);
    }

    private sealed record GroupMemberRow(Guid ResponderId, string FullName, string MobileNumber);

    private async Task<List<GroupMemberRow>> LoadActiveGroupMembersAsync(Guid groupId, CancellationToken ct)
    {
        var rows = await db.ResponderGroupMembers
            .AsNoTracking()
            .Where(x => x.GroupId == groupId && !x.Responder.IsDeleted)
            .Select(x => new
            {
                x.ResponderId,
                x.Responder.FullName,
                x.Responder.MobileNumber,
            })
            .ToListAsync(ct);

        return rows
            .GroupBy(x => x.ResponderId)
            .Select(g =>
            {
                var first = g.First();
                return new GroupMemberRow(g.Key, first.FullName ?? "", first.MobileNumber ?? "");
            })
            .OrderBy(x => x.FullName)
            .ToList();
    }

    private async Task<string> GenerateUniqueCodeAsync(CancellationToken ct)
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (var i = 0; i < 8; i++)
        {
            var code = new string(Enumerable.Range(0, 8).Select(_ => chars[Random.Shared.Next(chars.Length)]).ToArray());
            var exists = await db.FormDispatchLinks.AnyAsync(x => x.Code == code, ct);
            if (!exists) return code;
        }
        return Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }

    private async Task<bool> IsJobCancelledAsync(Guid jobId, CancellationToken ct) =>
        await db.FormDispatchGroupSendJobs.AsNoTracking()
            .AnyAsync(x => x.Id == jobId && x.Status == FormDispatchGroupSendJobStatus.Cancelled, ct);

    private async Task MarkJobCancelledAsync(FormDispatchGroupSendJob job, CancellationToken ct)
    {
        job.Status = FormDispatchGroupSendJobStatus.Cancelled;
        job.ErrorMessage ??= "لغو شده توسط کاربر";
        job.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    private static FormDispatchGroupSendStatusDto MapStatus(FormDispatchGroupSendJob job) =>
        new(
            job.Id,
            job.Status switch
            {
                FormDispatchGroupSendJobStatus.Queued => "queued",
                FormDispatchGroupSendJobStatus.Running => "running",
                FormDispatchGroupSendJobStatus.Completed => "completed",
                FormDispatchGroupSendJobStatus.Failed => "failed",
                FormDispatchGroupSendJobStatus.Cancelled => "cancelled",
                _ => "queued",
            },
            job.TotalCount,
            job.ProcessedCount,
            job.SentCount,
            job.FailedCount,
            job.ErrorMessage);
}
