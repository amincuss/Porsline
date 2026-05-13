using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/settings")]
public class AdminSecuritySettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet("security")]
    [Authorize(Policy = "settings.read")]
    public async Task<IActionResult> GetSecurity(CancellationToken cancellationToken)
    {
        var settings = await db.SecuritySettings.FirstOrDefaultAsync(cancellationToken) ?? new SecuritySettings();
        return Ok(new SecuritySettingsDto(
            settings.EnableRateLimiting,
            settings.MaxRequestsPerMinutePerIp,
            settings.MaxFailedOtpAttempts,
            settings.LockoutMinutes,
            settings.MaskAuthErrors,
            settings.LoginMethod));
    }

    [HttpPut("security")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> UpdateSecurity([FromBody] SecuritySettingsDto dto, CancellationToken cancellationToken)
    {
        var settings = await db.SecuritySettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new SecuritySettings();
            db.SecuritySettings.Add(settings);
        }

        settings.EnableRateLimiting = dto.EnableRateLimiting;
        settings.MaxRequestsPerMinutePerIp = Math.Clamp(dto.MaxRequestsPerMinutePerIp, 1, 500);
        settings.MaxFailedOtpAttempts = Math.Clamp(dto.MaxFailedOtpAttempts, 1, 20);
        settings.LockoutMinutes = Math.Clamp(dto.LockoutMinutes, 1, 120);
        settings.MaskAuthErrors = dto.MaskAuthErrors;
        settings.LoginMethod = dto.LoginMethod;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "تنظیمات امنیتی ذخیره شد" });
    }
}
