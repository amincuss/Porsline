using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/settings")]
public class AdminSiteSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet("site")]
    [Authorize(Policy = "settings.read")]
    public async Task<IActionResult> GetSite(CancellationToken cancellationToken)
    {
        var row = await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return Ok(new SiteSettingsDto(row?.PublicBaseUrl, row?.AdminPanelBaseUrl));
    }

    [HttpPut("site")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> UpdateSite([FromBody] SiteSettingsDto dto, CancellationToken cancellationToken)
    {
        var pub = NormalizeOptionalUrl(dto.PublicBaseUrl, out var pubErr);
        if (pubErr is not null) return BadRequest(new { message = pubErr });
        var adm = NormalizeOptionalUrl(dto.AdminPanelBaseUrl, out var admErr);
        if (admErr is not null) return BadRequest(new { message = admErr });

        var settings = await db.SiteSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new SiteSettings { Id = 1 };
            db.SiteSettings.Add(settings);
        }

        settings.PublicBaseUrl = pub;
        settings.AdminPanelBaseUrl = adm;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "تنظیمات سایت ذخیره شد" });
    }

    private static string? NormalizeOptionalUrl(string? raw, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var t = raw.Trim();
        var withScheme = t.Contains("://", StringComparison.Ordinal) ? t : $"https://{t}";
        if (!Uri.TryCreate(withScheme, UriKind.Absolute, out var u))
        {
            error = "آدرس معتبر نیست (مثال: https://example.com)";
            return null;
        }
        if (u.Scheme is not "http" and not "https")
        {
            error = "فقط آدرس http یا https مجاز است";
            return null;
        }
        return u.ToString().TrimEnd('/');
    }
}
