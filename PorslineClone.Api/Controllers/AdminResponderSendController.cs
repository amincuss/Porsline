using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Api.HangfireJobs;
using PorslineClone.Infrastructure.Services.FormDispatch;
using PorslineClone.Infrastructure.Services.SmsPatterns;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/responders/send")]
[Authorize]
public class AdminResponderSendController(
    AppDbContext db,
    FormDispatchGroupSendService dispatchService,
    IFormDispatchGroupSendEnqueue dispatchEnqueue,
    ISmsPatternService smsPatterns) : ControllerBase
{
    private Guid? CurrentUserGuid
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var g) ? g : null;
        }
    }

    [HttpGet("forms")]
    [Authorize(Policy = "responders.send.access")]
    public async Task<IActionResult> Forms([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var q = db.Forms
            .Where(x => !x.IsDeleted && x.IsActive)
            .ApplyVisibleForms(db, User);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x => x.Title.Contains(s) || (x.Description ?? "").Contains(s));
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Description,
                x.CreatedAtUtc,
                x.ApprovalEnabled,
                x.WorkflowTemplateId,
                x.WorkflowName,
                ActiveLinks = db.FormDispatchLinks.Count(l => l.FormId == x.Id && l.IsActive && l.UsedAtUtc == null && l.ExpiresAtUtc > DateTime.UtcNow),
                InactiveLinks = db.FormDispatchLinks.Count(l => l.FormId == x.Id && (!l.IsActive || l.ExpiresAtUtc <= DateTime.UtcNow) && l.UsedAtUtc == null)
            })
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling((double)total / pageSize) });
    }

    [HttpGet("sms-patterns")]
    [Authorize(Policy = "responders.send.access")]
    public async Task<IActionResult> DispatchSmsPatterns(CancellationToken ct)
    {
        await smsPatterns.EnsureSeededAsync(ct);
        var grouped = await smsPatterns.GetGroupedAsync(ct);
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "form.dispatch.link.default",
            "form.dispatch.link.manual",
        };
        var patterns = grouped
            .SelectMany(c => c.Patterns)
            .Where(p => keys.Contains(p.Key))
            .OrderBy(p => p.SortOrder)
            .Select(p => new
            {
                p.Key,
                p.Template,
                p.Placeholders,
                p.Description,
            })
            .ToList();
        return Ok(new { patterns });
    }

    [HttpGet("workflows")]
    [Authorize(Policy = "responders.send")]
    public async Task<IActionResult> Workflows(CancellationToken ct)
    {
        var rows = await db.FormWorkflowTemplates
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var items = rows.Select(x => new
        {
            x.Id,
            x.Name,
            approverCount = JsonSerializer.Deserialize<List<WorkflowStepDto>>(x.StepsJson ?? "[]")?.Count ?? 0,
        }).ToList();
        return Ok(items);
    }

    [HttpPost("activation")]
    [Authorize(Policy = "responders.send.activation")]
    public async Task<IActionResult> SetActivation([FromBody] FormDispatchActivationRequest req, CancellationToken ct)
    {
        if (req.FormId == Guid.Empty) return BadRequest(new { message = "فرم نامعتبر است" });
        if (req.Scope is not ("all" or "group" or "responder")) return BadRequest(new { message = "scope نامعتبر است" });

        var q = db.FormDispatchLinks
            .Where(x => x.FormId == req.FormId && x.UsedAtUtc == null && x.ExpiresAtUtc > DateTime.UtcNow);

        if (req.Scope == "group")
        {
            if (req.GroupId == Guid.Empty) return BadRequest(new { message = "گروه انتخاب نشده است" });
            var memberIds = await db.ResponderGroupMembers
                .Where(x => x.GroupId == req.GroupId)
                .Select(x => x.ResponderId)
                .Distinct()
                .ToListAsync(ct);
            q = q.Where(x => memberIds.Contains(x.ResponderId));
        }
        else if (req.Scope == "responder")
        {
            if (req.ResponderId == Guid.Empty) return BadRequest(new { message = "پاسخگو انتخاب نشده است" });
            q = q.Where(x => x.ResponderId == req.ResponderId);
        }

        var links = await q.ToListAsync(ct);
        foreach (var item in links) item.IsActive = req.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "وضعیت دسترسی فرم بروزرسانی شد", affected = links.Count, isActive = req.IsActive });
    }

    [HttpPost]
    [Authorize(Policy = "responders.send")]
    public async Task<IActionResult> Send([FromBody] SendFormDispatchRequest req, CancellationToken ct)
    {
        try
        {
            return await SendCoreAsync(req, ct);
        }
        catch (DbUpdateException ex) when (IsNationalCodeDuplicate(ex))
        {
            return BadRequest(new { message = "این کد ملی قبلاً ثبت شده است" });
        }
        catch (DbUpdateException ex) when (IsSchemaMismatch(ex))
        {
            return StatusCode(500, new
            {
                message = "ساختار دیتابیس با نسخهٔ API هم‌خوان نیست (ستون Gender یا SentByUserId). API را ری‌استارت کنید تا SchemaPatch اعمال شود.",
            });
        }
    }

    [HttpPost("bulk")]
    [Authorize(Policy = "responders.send")]
    public async Task<IActionResult> SendBulk([FromBody] BulkSendFormDispatchRequest req, CancellationToken ct)
    {
        try
        {
            return await SendBulkCoreAsync(req, ct);
        }
        catch (DbUpdateException ex) when (IsNationalCodeDuplicate(ex))
        {
            return BadRequest(new { message = "برخی کدهای ملی تکراری هستند" });
        }
        catch (DbUpdateException ex) when (IsSchemaMismatch(ex))
        {
            return StatusCode(500, new
            {
                message = "ساختار دیتابیس با نسخهٔ API هم‌خوان نیست. API را ری‌استارت کنید تا SchemaPatch اعمال شود.",
            });
        }
    }

    private static bool IsSchemaMismatch(DbUpdateException ex)
    {
        var text = ex.InnerException?.Message ?? ex.Message;
        return text.Contains("SentByUserId", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Invalid column name", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Gender", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNationalCodeDuplicate(DbUpdateException ex)
    {
        var text = ex.InnerException?.Message ?? ex.Message;
        return text.Contains("IX_Responders_NationalCode", StringComparison.OrdinalIgnoreCase)
            || (text.Contains("duplicate key", StringComparison.OrdinalIgnoreCase)
                && text.Contains("NationalCode", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<IActionResult> SendCoreAsync(SendFormDispatchRequest req, CancellationToken ct)
    {
        if (req.FormId == Guid.Empty) return BadRequest(new { message = "فرم نامعتبر است" });
        var form = await db.Forms
            .ApplyVisibleForms(db, User)
            .FirstOrDefaultAsync(x => x.Id == req.FormId && !x.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });
        if (!form.IsActive) return BadRequest(new { message = "این فرم غیرفعال است و قابل ارسال برای پاسخگو نیست" });
        if (form.ExpiresAtUtc.HasValue && form.ExpiresAtUtc.Value < DateTime.UtcNow)
            return BadRequest(new { message = "اعتبار این فرم به پایان رسیده و قابل ارسال نیست" });

        var responders = new List<(Guid Id, string FullName, string MobileNumber)>();
        if (string.Equals(req.Mode, "group", StringComparison.OrdinalIgnoreCase))
        {
            if (req.GroupId == Guid.Empty) return BadRequest(new { message = "گروه انتخاب نشده است" });

            var smsValidationGroup = ValidateSmsMessageOptions(req.SmsMessageMode, req.CustomSmsBody);
            if (smsValidationGroup is not null) return smsValidationGroup;

            var workflowResultGroup = await ResolveWorkflowTemplateAsync(req.SkipWorkflow, req.WorkflowTemplateId, ct);
            if (workflowResultGroup.Error is not null) return workflowResultGroup.Error;

            try
            {
                var job = await dispatchService.CreateGroupJobAsync(
                    form,
                    req.GroupId,
                    workflowResultGroup.Template,
                    req.SmsMessageMode,
                    req.CustomSmsBody,
                    CurrentUserGuid,
                    ct);
                var hangfireId = dispatchEnqueue.Enqueue(job.Id);
                await dispatchService.SetHangfireJobIdAsync(job.Id, hangfireId, ct);
                return Ok(new
                {
                    jobId = job.Id,
                    total = job.TotalCount,
                    message = "ارسال گروهی در پس‌زمینه شروع شد",
                    async = true,
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }
        else
        {
            var nationalCode = (req.NationalCode ?? "").Trim();
            var fullName = (req.FullName ?? "").Trim();
            var mobile = FormSubmissionMobileHelper.NormalizeMobile(req.MobileNumber);
            if (!ResponderLookupHelper.IsValidNationalCode(nationalCode))
                return BadRequest(new { message = "کد ملی الزامی است" });
            if (fullName.Length < 2) return BadRequest(new { message = "نام و نام خانوادگی نامعتبر است" });
            if (!ResponderLookupHelper.IsValidMobile(mobile))
                return BadRequest(new { message = "شماره موبایل معتبر نیست" });
            var gender = ResponderHonorific.ParseGender(req.Gender);
            if (req.Gender is { Length: > 0 } gRaw && gender is null)
                return BadRequest(new { message = "جنسیت معتبر نیست (آقای یا خانم)" });
            if (gender is null)
                return BadRequest(new { message = "جنسیت (آقای/خانم) الزامی است" });

            try
            {
                var responder = await ResponderLookupHelper.FindOrCreateForDispatchAsync(
                    db,
                    nationalCode,
                    fullName,
                    mobile,
                    gender,
                    CurrentUserGuid,
                    ct);
                responders.Add((responder.Id, fullName, mobile));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        if (responders.Count == 0) return BadRequest(new { message = "هیچ پاسخگویی برای ارسال یافت نشد" });

        var smsValidation = ValidateSmsMessageOptions(req.SmsMessageMode, req.CustomSmsBody);
        if (smsValidation is not null) return smsValidation;

        var workflowResult = await ResolveWorkflowTemplateAsync(req.SkipWorkflow, req.WorkflowTemplateId, ct);
        if (workflowResult.Error is not null) return workflowResult.Error;

        try
        {
            var dispatch = await dispatchService.DispatchToRespondersAsync(
                form,
                responders,
                workflowResult.Template,
                req.SmsMessageMode,
                req.CustomSmsBody,
                CurrentUserGuid,
                ct,
                smsSource: "form.dispatch.single");
            if (dispatch.Sent == 0)
                return BadRequest(new { message = "ارسال پیامک ناموفق بود؛ لاگ پیامک را بررسی کنید", sent = dispatch.Sent, failed = dispatch.Failed, total = responders.Count });
            return Ok(new { message = "ارسال انجام شد", sent = dispatch.Sent, failed = dispatch.Failed, total = responders.Count });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private async Task<IActionResult> SendBulkCoreAsync(BulkSendFormDispatchRequest req, CancellationToken ct)
    {
        if (req.FormId == Guid.Empty) return BadRequest(new { message = "فرم نامعتبر است" });
        if (req.Rows is null or { Count: 0 }) return BadRequest(new { message = "ردیفی برای ارسال وجود ندارد" });
        if (req.Rows.Count > 500) return BadRequest(new { message = "حداکثر ۵۰۰ ردیف در هر بار ارسال مجاز است" });

        var form = await db.Forms
            .ApplyVisibleForms(db, User)
            .FirstOrDefaultAsync(x => x.Id == req.FormId && !x.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });
        if (!form.IsActive) return BadRequest(new { message = "این فرم غیرفعال است و قابل ارسال برای پاسخگو نیست" });
        if (form.ExpiresAtUtc.HasValue && form.ExpiresAtUtc.Value < DateTime.UtcNow)
            return BadRequest(new { message = "اعتبار این فرم به پایان رسیده و قابل ارسال نیست" });

        var workflowResult = await ResolveWorkflowTemplateAsync(req.SkipWorkflow, req.WorkflowTemplateId, ct);
        if (workflowResult.Error is not null) return workflowResult.Error;

        var responders = new List<(Guid Id, string FullName, string MobileNumber)>();
        var invalidCount = 0;
        var skippedCount = 0;

        foreach (var row in req.Rows)
        {
            var firstName = (row.FirstName ?? "").Trim();
            var lastName = (row.LastName ?? "").Trim();
            var fullName = string.IsNullOrWhiteSpace(row.FullName)
                ? $"{firstName} {lastName}".Trim()
                : row.FullName.Trim();
            var nationalCode = (row.NationalCode ?? "").Trim();
            var mobile = FormSubmissionMobileHelper.NormalizeMobile(row.MobileNumber);
            var gender = ResponderHonorific.ParseGender(row.Gender);

            if (string.IsNullOrWhiteSpace(firstName) && string.IsNullOrWhiteSpace(lastName) && fullName.Length < 2)
            {
                skippedCount++;
                continue;
            }

            if (!ResponderLookupHelper.IsValidNationalCode(nationalCode)
                || fullName.Length < 2
                || !ResponderLookupHelper.IsValidMobile(mobile)
                || gender is null)
            {
                invalidCount++;
                continue;
            }

            try
            {
                var responder = await ResponderLookupHelper.FindOrCreateForDispatchAsync(
                    db,
                    nationalCode,
                    fullName,
                    mobile,
                    gender,
                    CurrentUserGuid,
                    ct);
                responders.Add((responder.Id, fullName, mobile));
            }
            catch (InvalidOperationException)
            {
                invalidCount++;
            }
        }

        if (responders.Count == 0)
            return BadRequest(new { message = "هیچ ردیف معتبری برای ارسال یافت نشد", invalidCount, skippedCount });

        var smsValidation = ValidateSmsMessageOptions(req.SmsMessageMode, req.CustomSmsBody);
        if (smsValidation is not null) return smsValidation;

        try
        {
            var dispatch = await dispatchService.DispatchToRespondersAsync(
                form,
                responders,
                workflowResult.Template,
                req.SmsMessageMode,
                req.CustomSmsBody,
                CurrentUserGuid,
                ct);
            return Ok(new
            {
                message = "ارسال گروهی از اکسل انجام شد",
                sent = dispatch.Sent,
                failed = dispatch.Failed,
                total = responders.Count,
                invalidCount,
                skippedCount,
                processedRows = req.Rows.Count,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private async Task<(FormWorkflowTemplate? Template, IActionResult? Error)> ResolveWorkflowTemplateAsync(
        bool skipWorkflow,
        Guid workflowTemplateId,
        CancellationToken ct)
    {
        if (skipWorkflow) return (null, null);
        if (workflowTemplateId == Guid.Empty)
            return (null, BadRequest(new { message = "گردش تأیید را انتخاب کنید یا گزینه «بدون گردش» را فعال کنید" }));
        var workflowTemplate = await db.FormWorkflowTemplates
            .FirstOrDefaultAsync(x => x.Id == workflowTemplateId && x.IsActive, ct);
        if (workflowTemplate is null)
            return (null, BadRequest(new { message = "گردش انتخاب‌شده یافت نشد یا غیرفعال است" }));
        return (workflowTemplate, null);
    }

    private static IActionResult? ValidateSmsMessageOptions(string? smsMessageMode, string? customSmsBody)
    {
        if (!string.Equals(smsMessageMode, "manual", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrWhiteSpace(customSmsBody))
            return new BadRequestObjectResult(new { message = "متن پیامک دستی را وارد کنید" });
        return null;
    }

    [HttpGet("group-jobs/active")]
    [Authorize(Policy = "responders.send")]
    public async Task<IActionResult> GetActiveGroupSendJob(CancellationToken ct)
    {
        var status = await dispatchService.GetActiveJobForUserAsync(CurrentUserGuid, ct);
        return Ok(status);
    }

    [HttpGet("group-jobs/{jobId:guid}")]
    [Authorize(Policy = "responders.send")]
    public async Task<IActionResult> GetGroupSendJobStatus(Guid jobId, CancellationToken ct)
    {
        var status = await dispatchService.GetStatusAsync(jobId, ct);
        if (status is null) return NotFound(new { message = "کار یافت نشد" });
        return Ok(status);
    }

    [HttpPost("group-jobs/{jobId:guid}/cancel")]
    [Authorize(Policy = "responders.send")]
    public async Task<IActionResult> CancelGroupSendJob(Guid jobId, CancellationToken ct)
    {
        var (cancelled, hangfireJobId) = await dispatchService.CancelJobAsync(jobId, ct);
        if (!cancelled)
            return BadRequest(new { message = "این کار قابل لغو نیست یا قبلاً تمام شده است" });

        dispatchEnqueue.TryCancel(hangfireJobId);
        return Ok(new { message = "ارسال لغو شد" });
    }
}

public class SendFormDispatchRequest
{
    public Guid FormId { get; set; }
    public string Mode { get; set; } = "single"; // single | group
    public Guid GroupId { get; set; }
    public Guid ResponderId { get; set; }
    public string? NationalCode { get; set; }
    public string? FullName { get; set; }
    public string? MobileNumber { get; set; }
    /// <summary>male | female — برای پیام «آقای/خانم»</summary>
    public string? Gender { get; set; }
    /// <summary>قالب گردش؛ پس از ثبت کامل فرم به‌صورت خودکار شروع می‌شود.</summary>
    public Guid WorkflowTemplateId { get; set; }
    /// <summary>ارسال بدون گردش — انتصاب بعداً از «فرم کاربران».</summary>
    public bool SkipWorkflow { get; set; }
    /// <summary>auto | manual — پیش‌فرض auto</summary>
    public string? SmsMessageMode { get; set; }
    /// <summary>متن پیامک دستی؛ لینک فرم در انتها خودکار اضافه می‌شود.</summary>
    public string? CustomSmsBody { get; set; }
}

public class FormDispatchActivationRequest
{
    public Guid FormId { get; set; }
    public string Scope { get; set; } = "all"; // all | group | responder
    public Guid GroupId { get; set; }
    public Guid ResponderId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class BulkSendFormDispatchRequest
{
    public Guid FormId { get; set; }
    public bool SkipWorkflow { get; set; }
    public Guid WorkflowTemplateId { get; set; }
    public string? SmsMessageMode { get; set; }
    public string? CustomSmsBody { get; set; }
    public List<BulkSendFormRow> Rows { get; set; } = [];
}

public class BulkSendFormRow
{
    public int RowNumber { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? FullName { get; set; }
    public string? NationalCode { get; set; }
    public string? MobileNumber { get; set; }
    public string? Gender { get; set; }
}

