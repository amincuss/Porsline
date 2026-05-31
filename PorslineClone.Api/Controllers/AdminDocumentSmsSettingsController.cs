using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/documents/settings")]
public class AdminDocumentSmsSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet("sms")]
    [Authorize(Policy = "settings.read")]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var settings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();
        return Ok(MapDto(settings));
    }

    [HttpPut("sms")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> Update([FromBody] DocumentSmsSettingsDto dto, CancellationToken ct)
    {
        var settings = await db.SmsSettings.FirstOrDefaultAsync(ct);
        if (settings is null)
        {
            settings = new SmsSettings();
            db.SmsSettings.Add(settings);
        }

        settings.DocumentApprovalReferralSmsEnabled = dto.DocumentApprovalReferralSmsEnabled;
        settings.DocumentOwnerStepApprovalNotifySmsEnabled = dto.DocumentOwnerStepApprovalNotifySmsEnabled;
        settings.DocumentWorkflowCompletedOwnerSmsEnabled = dto.DocumentWorkflowCompletedOwnerSmsEnabled;
        settings.DocumentWorkflowRejectedOwnerSmsEnabled = dto.DocumentWorkflowRejectedOwnerSmsEnabled;
        settings.DocumentPostApprovalAssigneeSmsEnabled = dto.DocumentPostApprovalAssigneeSmsEnabled;
        settings.DocumentApprovalReminderSmsEnabled = dto.DocumentApprovalReminderSmsEnabled;
        settings.DocumentApprovalReminderDelayDays = Math.Max(0, dto.DocumentApprovalReminderDelayDays);
        settings.DocumentApprovalReminderDelayHours = Math.Max(0, dto.DocumentApprovalReminderDelayHours);
        if (settings.DocumentApprovalReminderDelayDays == 0 && settings.DocumentApprovalReminderDelayHours == 0)
            settings.DocumentApprovalReminderDelayHours = 24;

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "تنظیمات پیامک اسناد ذخیره شد" });
    }

    private static DocumentSmsSettingsDto MapDto(SmsSettings settings) => new(
        settings.DocumentApprovalReferralSmsEnabled,
        settings.DocumentOwnerStepApprovalNotifySmsEnabled,
        settings.DocumentWorkflowCompletedOwnerSmsEnabled,
        settings.DocumentWorkflowRejectedOwnerSmsEnabled,
        settings.DocumentPostApprovalAssigneeSmsEnabled,
        settings.DocumentApprovalReminderSmsEnabled,
        settings.DocumentApprovalReminderDelayDays,
        settings.DocumentApprovalReminderDelayHours);
}
