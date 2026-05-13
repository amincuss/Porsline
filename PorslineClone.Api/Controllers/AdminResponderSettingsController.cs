using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/settings")]
public class AdminResponderSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet("responders")]
    [Authorize(Policy = "settings.read")]
    public async Task<IActionResult> GetResponderSettings(CancellationToken cancellationToken)
    {
        var settings = await db.SmsSettings.FirstOrDefaultAsync(cancellationToken);
        return Ok(new
        {
            publicFormRequireOtp = settings?.PublicFormRequireOtp ?? false
        });
    }

    [HttpPut("responders")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> UpdateResponderSettings([FromBody] ResponderPublicSettingsDto dto, CancellationToken cancellationToken)
    {
        var settings = await db.SmsSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new Domain.Entities.SmsSettings();
            db.SmsSettings.Add(settings);
        }

        settings.PublicFormRequireOtp = dto.PublicFormRequireOtp;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "تنظیمات پاسخگو ذخیره شد" });
    }
}

public record ResponderPublicSettingsDto(bool PublicFormRequireOtp);

