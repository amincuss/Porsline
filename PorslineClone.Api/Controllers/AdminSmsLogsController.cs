using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/settings/sms-logs")]
[Authorize]
public class AdminSmsLogsController(AppDbContext db, ISmsSender smsSender) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "settings.sms.logs.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? mobile = null,
        [FromQuery] string? search = null,
        [FromQuery] string? source = null,
        [FromQuery] DateTime? fromUtc = null,
        [FromQuery] DateTime? toUtc = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 100);

        var q = ApplyFilters(db.SmsLogs.AsNoTracking(), status, mobile, search, source, fromUtc, toUtc);

        var total = await q.CountAsync(ct);
        var successCount = await q.CountAsync(x => x.IsSuccess, ct);
        var failedCount = total - successCount;

        var items = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new SmsLogItemDto(
                x.Id,
                x.MobileNumber,
                x.Message,
                x.IsSuccess,
                x.ErrorMessage,
                x.TechnicalDetail,
                x.Source,
                x.HttpStatusCode,
                x.CreatedAtUtc))
            .ToListAsync(ct);

        return Ok(new SmsLogListResponse(
            items,
            total,
            page,
            pageSize,
            (int)Math.Ceiling((double)total / pageSize),
            successCount,
            failedCount));
    }

    [HttpGet("sources")]
    [Authorize(Policy = "settings.sms.logs.read")]
    public async Task<IActionResult> Sources(CancellationToken ct)
    {
        var sources = await db.SmsLogs.AsNoTracking()
            .Where(x => x.Source != null && x.Source != "")
            .Select(x => x.Source!)
            .Distinct()
            .OrderBy(x => x)
            .Take(100)
            .ToListAsync(ct);
        return Ok(new { sources });
    }

    [HttpPost("{id:guid}/resend")]
    [Authorize(Policy = "settings.sms.logs.read")]
    public async Task<IActionResult> Resend(Guid id, CancellationToken ct)
    {
        var log = await db.SmsLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (log is null) return NotFound(new { message = "لاگ پیامک یافت نشد" });

        var mobile = (log.MobileNumber ?? "").Trim();
        var message = log.Message ?? "";
        if (!ResponderLookupHelper.IsValidMobile(mobile))
            return BadRequest(new { message = "شماره موبایل این لاگ معتبر نیست" });
        if (string.IsNullOrWhiteSpace(message))
            return BadRequest(new { message = "متن پیامک خالی است" });

        var ok = await smsSender.SendSmsAsync(
            new SmsRequest(mobile, message, log.Source, log.Id), ct);

        var updated = await db.SmsLogs.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);

        return Ok(new SmsLogResendResponse(
            ok,
            ok ? "پیامک مجدداً ارسال شد" : "ارسال مجدد ناموفق بود",
            updated?.ErrorMessage,
            id));
    }

    [HttpDelete("all")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> DeleteAll(CancellationToken ct)
    {
        var deleted = await db.SmsLogs.ExecuteDeleteAsync(ct);
        return Ok(new { message = $"{deleted} لاگ پیامک حذف شد", deleted });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> DeleteOne(Guid id, CancellationToken ct)
    {
        var log = await db.SmsLogs.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (log is null) return NotFound(new { message = "لاگ پیامک یافت نشد" });

        db.SmsLogs.Remove(log);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "لاگ پیامک حذف شد", deleted = 1 });
    }

    private static IQueryable<Domain.Entities.SmsLog> ApplyFilters(
        IQueryable<Domain.Entities.SmsLog> q,
        string? status,
        string? mobile,
        string? search,
        string? source,
        DateTime? fromUtc,
        DateTime? toUtc)
    {
        if (string.Equals(status, "success", StringComparison.OrdinalIgnoreCase))
            q = q.Where(x => x.IsSuccess);
        else if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
            q = q.Where(x => !x.IsSuccess);

        if (!string.IsNullOrWhiteSpace(mobile))
        {
            var m = mobile.Trim();
            q = q.Where(x => x.MobileNumber.Contains(m));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                x.Message.Contains(s)
                || (x.ErrorMessage != null && x.ErrorMessage.Contains(s))
                || (x.Source != null && x.Source.Contains(s)));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            var src = source.Trim();
            q = q.Where(x => x.Source != null && x.Source.Contains(src));
        }

        if (fromUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            q = q.Where(x => x.CreatedAtUtc <= toUtc.Value);

        return q;
    }
}
