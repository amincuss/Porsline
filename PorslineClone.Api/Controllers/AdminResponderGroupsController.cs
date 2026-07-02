using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.FormDispatch;
using PorslineClone.Api.HangfireJobs;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/responder-groups")]
[Authorize]
public class AdminResponderGroupsController(
    AppDbContext db,
    ResponderGroupSmsInquiryService smsInquiry,
    ISmsSender smsSender,
    FormDispatchGroupSendService dispatchService,
    IFormDispatchGroupSendEnqueue dispatchEnqueue) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "respondergroups.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.ResponderGroups.Where(x => !x.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(x => x.Name.Contains(q));
        }

        query = sortBy switch
        {
            "name_asc" => query.OrderBy(x => x.Name),
            "name_desc" => query.OrderByDescending(x => x.Name),
            "created_asc" => query.OrderBy(x => x.CreatedAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.IsActive,
                x.CreatedAtUtc,
                MemberCount = x.Members.Count(m => !m.Responder.IsDeleted)
            })
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling((double)total / pageSize) });
    }

    [HttpGet("options")]
    [Authorize(Policy = "respondergroups.read")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        var items = await db.ResponderGroups
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);
        return Ok(items);
    }

    /// <summary>لیست سبک گروه‌ها برای سایدبار صفحه پاسخگوها.</summary>
    [HttpGet("sidebar")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> Sidebar(CancellationToken ct)
    {
        var items = await db.ResponderGroups
            .AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                MemberCount = x.Members.Count(m => !m.Responder.IsDeleted),
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = "respondergroups.add")]
    public async Task<IActionResult> Create([FromBody] ResponderGroupUpsertDto dto, CancellationToken ct)
    {
        var name = dto.Name.Trim();
        if (name.Length < 2) return BadRequest(new { message = "نام گروه نامعتبر است" });
        if (await db.ResponderGroups.AnyAsync(x => x.Name == name && !x.IsDeleted, ct))
            return BadRequest(new { message = "این نام گروه قبلا ثبت شده است" });

        db.ResponderGroups.Add(new ResponderGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = dto.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه ثبت شد" });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "respondergroups.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ResponderGroupUpsertDto dto, CancellationToken ct)
    {
        var item = await db.ResponderGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null || item.IsDeleted) return NotFound(new { message = "گروه یافت نشد" });
        var name = dto.Name.Trim();
        if (name.Length < 2) return BadRequest(new { message = "نام گروه نامعتبر است" });
        if (await db.ResponderGroups.AnyAsync(x => x.Name == name && x.Id != id && !x.IsDeleted, ct))
            return BadRequest(new { message = "این نام گروه قبلا ثبت شده است" });
        item.Name = name;
        item.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه بروزرسانی شد" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "respondergroups.delete")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        var item = await db.ResponderGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null || item.IsDeleted) return NotFound(new { message = "گروه یافت نشد" });
        item.IsDeleted = true;
        item.IsActive = false;
        item.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه به‌صورت حذف نرم از لیست حذف شد" });
    }

    /// <summary>استعلام وضعیت پیامک ارسال فرم برای اعضای گروه.</summary>
    [HttpGet("{id:guid}/sms-inquiry")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> SmsInquiry(
        Guid id,
        [FromQuery] Guid? formId,
        [FromQuery] bool onlyIncomplete = true,
        CancellationToken ct = default)
    {
        try
        {
            var result = await smsInquiry.GetAsync(id, formId, onlyIncomplete, ct);
            if (result is null) return NotFound(new { message = "گروه یافت نشد" });
            return Ok(result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return StatusCode(408, new { message = "زمان استعلام تمام شد؛ لطفاً دوباره تلاش کنید" });
        }
    }

    /// <summary>پیش‌نمایش ارسال — چند نفر واجد شرایط‌اند و چند نفر قبلاً ثبت کرده‌اند.</summary>
    [HttpGet("{id:guid}/send-preview")]
    [Authorize(Policy = "responders.send")]
    public async Task<IActionResult> SendPreview(Guid id, [FromQuery] Guid formId, CancellationToken ct)
    {
        if (formId == Guid.Empty) return BadRequest(new { message = "فرم را انتخاب کنید" });
        var preview = await dispatchService.GetSendPreviewAsync(id, formId, ct);
        if (preview is null) return NotFound(new { message = "گروه یا فرم یافت نشد" });
        return Ok(preview);
    }

    /// <summary>ارسال پیامک لینک فرم به اعضایی که هنوز فرم را تکمیل نکرده‌اند — پس‌زمینه Hangfire.</summary>
    [HttpPost("{id:guid}/sms-inquiry/send-incomplete")]
    [Authorize(Policy = "responders.send")]
    public async Task<IActionResult> SendIncompleteSmsInquiry(
        Guid id,
        [FromQuery] Guid? formId,
        CancellationToken ct)
    {
        Guid? createdBy = null;
        var userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (Guid.TryParse(userIdStr, out var uid)) createdBy = uid;

        try
        {
            var job = await dispatchService.CreateIncompleteGroupJobAsync(id, formId, createdBy, ct);
            var hangfireId = dispatchEnqueue.Enqueue(job.Id);
            await dispatchService.SetHangfireJobIdAsync(job.Id, hangfireId, ct);
            return Ok(new
            {
                jobId = job.Id,
                total = job.TotalCount,
                message = "ارسال پیامک به تکمیل‌نکرده‌ها در پس‌زمینه شروع شد",
                async = true,
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>ارسال مجدد پیامک استعلام‌شده — همان ردیف لاگ بروزرسانی می‌شود.</summary>
    [HttpPost("sms-inquiry/{logId:guid}/resend")]
    public async Task<IActionResult> ResendSmsInquiry(Guid logId, CancellationToken ct)
    {
        if (!User.HasClaim("permission", "responders.send")
            && !User.HasClaim("permission", "responders.update")
            && !User.HasClaim("permission", "settings.sms.logs.read"))
        {
            return Forbid();
        }

        var log = await db.SmsLogs.FirstOrDefaultAsync(x => x.Id == logId, ct);
        if (log is null) return NotFound(new { message = "لاگ پیامک یافت نشد" });

        var mobile = (log.MobileNumber ?? "").Trim();
        var message = log.Message ?? "";
        if (!ResponderLookupHelper.IsValidMobile(mobile))
            return BadRequest(new { message = "شماره موبایل این لاگ معتبر نیست" });
        if (string.IsNullOrWhiteSpace(message))
            return BadRequest(new { message = "متن پیامک خالی است" });

        var ok = await smsSender.SendSmsAsync(
            new SmsRequest(mobile, message, log.Source, log.Id), ct);

        var updated = await db.SmsLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == logId, ct);

        return Ok(new
        {
            isSuccess = ok,
            message = ok ? "پیامک مجدداً ارسال شد" : "ارسال مجدد ناموفق بود",
            errorMessage = updated?.ErrorMessage,
            smsLogId = logId,
            isSuccessUpdated = updated?.IsSuccess,
            sentAtUtc = updated?.CreatedAtUtc,
        });
    }
}

public record ResponderGroupUpsertDto(string Name, bool IsActive = true);
