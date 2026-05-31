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

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/responders/send")]
[Authorize]
public class AdminResponderSendController(
    AppDbContext db,
    ISmsSender smsSender,
    IFrontendUrlResolver frontendUrls) : ControllerBase
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
            responders = await db.ResponderGroupMembers
                .Where(x => x.GroupId == req.GroupId && !x.Responder.IsDeleted)
                .Select(x => new ValueTuple<Guid, string, string>(x.Responder.Id, x.Responder.FullName, x.Responder.MobileNumber))
                .Distinct()
                .ToListAsync(ct);
        }
        else
        {
            var nationalCode = (req.NationalCode ?? "").Trim();
            var fullName = (req.FullName ?? "").Trim();
            var mobile = (req.MobileNumber ?? "").Trim();
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
                responders.Add((responder.Id, responder.FullName, responder.MobileNumber));
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { message = ex.Message });
            }
        }

        if (responders.Count == 0) return BadRequest(new { message = "هیچ پاسخگویی برای ارسال یافت نشد" });

        var workflowResult = await ResolveWorkflowTemplateAsync(req.SkipWorkflow, req.WorkflowTemplateId, ct);
        if (workflowResult.Error is not null) return workflowResult.Error;

        try
        {
            var dispatch = await DispatchFormToRespondersAsync(form, responders, workflowResult.Template, ct);
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
            var mobile = (row.MobileNumber ?? "").Trim();
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
                responders.Add((responder.Id, responder.FullName, responder.MobileNumber));
            }
            catch (InvalidOperationException)
            {
                invalidCount++;
            }
        }

        if (responders.Count == 0)
            return BadRequest(new { message = "هیچ ردیف معتبری برای ارسال یافت نشد", invalidCount, skippedCount });

        try
        {
            var dispatch = await DispatchFormToRespondersAsync(form, responders, workflowResult.Template, ct);
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

    private async Task<(int Sent, int Failed)> DispatchFormToRespondersAsync(
        Form form,
        IReadOnlyList<(Guid Id, string FullName, string MobileNumber)> responders,
        FormWorkflowTemplate? workflowTemplate,
        CancellationToken ct)
    {
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

        foreach (var r in responders)
        {
            if (string.IsNullOrWhiteSpace(r.MobileNumber)) { failed++; continue; }
            var code = await GenerateUniqueCodeAsync(ct);
            db.FormDispatchLinks.Add(new FormDispatchLink
            {
                Id = Guid.NewGuid(),
                FormId = form.Id,
                ResponderId = r.Id,
                ResponderMobileNumber = r.MobileNumber,
                ResponderFullName = r.FullName,
                Code = code,
                ExpiresAtUtc = linkExpiry,
                WorkflowTemplateId = workflowTemplate?.Id,
                SentByUserId = CurrentUserGuid,
            });
            var link = $"{baseUrl}/forms/fill?c={code}";
            var msg = $"سلام {r.FullName}\nفرم «{form.Title}» برای شما ارسال شد.\nلطفا از لینک زیر تکمیل کنید:\n{link}";
            var ok = await smsSender.SendSmsAsync(new SmsRequest(r.MobileNumber, msg), ct);
            if (ok) sent++; else failed++;
        }

        await db.SaveChangesAsync(ct);
        return (sent, failed);
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

