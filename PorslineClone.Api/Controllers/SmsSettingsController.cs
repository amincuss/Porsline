using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/settings")]
public class AdminSmsSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet("sms")]
    [Authorize(Policy = "settings.read")]
    public async Task<IActionResult> GetSms(CancellationToken cancellationToken)
    {
        var settings = await db.SmsSettings.FirstOrDefaultAsync(cancellationToken) ?? new SmsSettings();
        return Ok(new SmsSettingsDto(
            settings.OtpEnabled,
            settings.SurveySendEnabled,
            settings.SurveyCompletedNotificationEnabled,
            settings.UserCreateSmsEnabled,
            settings.ApprovalReferralSmsEnabled,
            settings.ContractCreatorApprovalNotifySmsEnabled));
    }

    [HttpPut("sms")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> UpdateSms([FromBody] SmsSettingsDto dto, CancellationToken cancellationToken)
    {
        var settings = await db.SmsSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is null)
        {
            settings = new SmsSettings();
            db.SmsSettings.Add(settings);
        }

        settings.OtpEnabled = dto.OtpEnabled;
        settings.SurveySendEnabled = dto.SurveySendEnabled;
        settings.SurveyCompletedNotificationEnabled = dto.SurveyCompletedNotificationEnabled;
        settings.UserCreateSmsEnabled = dto.UserCreateSmsEnabled;
        settings.ApprovalReferralSmsEnabled = dto.ApprovalReferralSmsEnabled;
        settings.ContractCreatorApprovalNotifySmsEnabled = dto.ContractCreatorApprovalNotifySmsEnabled;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "تنظیمات پیامک ذخیره شد" });
    }
}

