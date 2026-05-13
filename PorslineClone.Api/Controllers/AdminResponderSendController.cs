using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/responders/send")]
[Authorize]
public class AdminResponderSendController(
    AppDbContext db,
    ISmsSender smsSender,
    IFrontendUrlResolver frontendUrls) : ControllerBase
{

    [HttpGet("forms")]
    [Authorize(Policy = "responders.send")]
    public async Task<IActionResult> Forms([FromQuery] int page = 1, [FromQuery] int pageSize = 10, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);

        var q = db.Forms.Where(x => !x.IsDeleted && x.IsActive);
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
                ActiveLinks = db.FormDispatchLinks.Count(l => l.FormId == x.Id && l.IsActive && l.UsedAtUtc == null && l.ExpiresAtUtc > DateTime.UtcNow),
                InactiveLinks = db.FormDispatchLinks.Count(l => l.FormId == x.Id && (!l.IsActive || l.ExpiresAtUtc <= DateTime.UtcNow) && l.UsedAtUtc == null)
            })
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling((double)total / pageSize) });
    }

    [HttpPost("activation")]
    [Authorize(Policy = "responders.update")]
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
        if (req.FormId == Guid.Empty) return BadRequest(new { message = "فرم نامعتبر است" });
        var form = await db.Forms.FirstOrDefaultAsync(x => x.Id == req.FormId && !x.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });
        if (!form.IsActive) return BadRequest(new { message = "این فرم غیرفعال است و قابل ارسال برای پاسخگو نیست" });
        if (form.ExpiresAtUtc.HasValue && form.ExpiresAtUtc.Value < DateTime.UtcNow)
            return BadRequest(new { message = "اعتبار این فرم به پایان رسیده و قابل ارسال نیست" });

        var responders = new List<(Guid Id, string FullName, string MobileNumber)>();
        if (req.Mode == "group")
        {
            if (req.GroupId == Guid.Empty) return BadRequest(new { message = "گروه انتخاب نشده است" });
            responders = await db.ResponderGroupMembers
                .Where(x => x.GroupId == req.GroupId)
                .Select(x => new ValueTuple<Guid, string, string>(x.Responder.Id, x.Responder.FullName, x.Responder.MobileNumber))
                .Distinct()
                .ToListAsync(ct);
        }
        else
        {
            var fullName = (req.FullName ?? "").Trim();
            var mobile = (req.MobileNumber ?? "").Trim();
            if (fullName.Length < 2) return BadRequest(new { message = "نام و نام خانوادگی نامعتبر است" });
            if (!System.Text.RegularExpressions.Regex.IsMatch(mobile, "^09\\d{9}$"))
                return BadRequest(new { message = "شماره موبایل معتبر نیست" });

            // اگر پاسخگو قبلاً وجود داشت آپدیت می‌شود، در غیر این صورت ساخته می‌شود
            var responder = await db.Responders.FirstOrDefaultAsync(x => x.MobileNumber == mobile, ct);
            if (responder is null)
            {
                responder = new Domain.Entities.Responder
                {
                    Id = Guid.NewGuid(),
                    FullName = fullName,
                    MobileNumber = mobile,
                    CreatedAtUtc = DateTime.UtcNow
                };
                db.Responders.Add(responder);
                await db.SaveChangesAsync(ct);
            }
            else
            {
                responder.FullName = fullName;
                await db.SaveChangesAsync(ct);
            }
            responders.Add((responder.Id, responder.FullName, responder.MobileNumber));
        }

        if (responders.Count == 0) return BadRequest(new { message = "هیچ پاسخگویی برای ارسال یافت نشد" });

        var baseUrl = await frontendUrls.ResolvePublicBaseUrlAsync(ct);
        if (string.IsNullOrWhiteSpace(baseUrl))
            return BadRequest(new { message = "آدرس پایهٔ عمومی در تنظیمات سایت یا appsettings (Frontend:BaseUrl) تعریف نشده است" });
        var sent = 0;
        var failed = 0;

        foreach (var r in responders)
        {
            if (string.IsNullOrWhiteSpace(r.MobileNumber)) { failed++; continue; }
            var code = await GenerateUniqueCodeAsync(ct);
            var defaultExpiry = DateTime.UtcNow.AddDays(7);
            var linkExpiry = form.ExpiresAtUtc.HasValue && form.ExpiresAtUtc.Value < defaultExpiry
                ? form.ExpiresAtUtc.Value
                : defaultExpiry;
            db.FormDispatchLinks.Add(new Domain.Entities.FormDispatchLink
            {
                Id = Guid.NewGuid(),
                FormId = form.Id,
                ResponderId = r.Id,
                ResponderMobileNumber = r.MobileNumber,
                ResponderFullName = r.FullName,
                Code = code,
                ExpiresAtUtc = linkExpiry
            });
            var link = $"{baseUrl}/forms/fill?c={code}";
            var msg = $"سلام {r.FullName}\nفرم «{form.Title}» برای شما ارسال شد.\nلطفا از لینک زیر تکمیل کنید:\n{link}";
            var ok = await smsSender.SendSmsAsync(new SmsRequest(r.MobileNumber, msg), ct);
            if (ok) sent++; else failed++;
        }

        await db.SaveChangesAsync(ct);

        return Ok(new { message = "ارسال انجام شد", sent, failed, total = responders.Count });
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
    public string? FullName { get; set; }
    public string? MobileNumber { get; set; }
}

public class FormDispatchActivationRequest
{
    public Guid FormId { get; set; }
    public string Scope { get; set; } = "all"; // all | group | responder
    public Guid GroupId { get; set; }
    public Guid ResponderId { get; set; }
    public bool IsActive { get; set; } = true;
}

