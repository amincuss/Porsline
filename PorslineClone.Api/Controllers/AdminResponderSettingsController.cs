using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/settings")]
public class AdminResponderSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet("responders")]
    [Authorize(Policy = "settings.read")]
    public async Task<IActionResult> GetResponderSettings(CancellationToken cancellationToken)
    {
        var security = await db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var sms = await db.SmsSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        return Ok(new
        {
            publicFormRequireOtp = SecuritySettingsHelper.DispatchLinkRequiresOtp(security ?? new Domain.Entities.SecuritySettings(), sms)
        });
    }

    [HttpPut("responders")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> UpdateResponderSettings([FromBody] ResponderPublicSettingsDto dto, CancellationToken cancellationToken)
    {
        var security = await db.SecuritySettings.FirstOrDefaultAsync(cancellationToken);
        if (security is null)
        {
            security = new Domain.Entities.SecuritySettings();
            db.SecuritySettings.Add(security);
        }

        security.DispatchLinkRequireOtp = dto.PublicFormRequireOtp;

        var sms = await db.SmsSettings.FirstOrDefaultAsync(cancellationToken);
        if (sms is not null)
            sms.PublicFormRequireOtp = dto.PublicFormRequireOtp;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "تنظیمات پاسخگو ذخیره شد" });
    }
}

public record ResponderPublicSettingsDto(bool PublicFormRequireOtp);

