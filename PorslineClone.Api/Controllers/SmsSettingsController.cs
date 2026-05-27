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
            settings.FormWorkflowCompletedSenderSmsEnabled,
            settings.FormActionPhaseCompletedSenderSmsEnabled,
            settings.FormResponderApprovedSmsEnabled,
            settings.FormWorkflowRejectedSenderSmsEnabled,
            settings.FormWorkflowRejectedResponderSmsEnabled,
            settings.ContractCreatorApprovalNotifySmsEnabled,
            settings.ContractAmendmentAssigneeSmsEnabled,
            settings.ContractAmendmentReturnToRejecterSmsEnabled,
            settings.ContractRejectionNotifySmsEnabled,
            settings.ContractActionCompletedCreatorSmsEnabled,
            settings.ApprovalReminderSmsEnabled,
            settings.ApprovalReminderDelayDays,
            settings.ApprovalReminderDelayHours,
            settings.WorkflowValidityReminderSmsEnabled,
            settings.WorkflowValiditySuspensionDelayDays,
            settings.WorkflowValiditySuspensionDelayHours));
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
        settings.FormWorkflowCompletedSenderSmsEnabled = dto.FormWorkflowCompletedSenderSmsEnabled;
        settings.FormActionPhaseCompletedSenderSmsEnabled = dto.FormActionPhaseCompletedSenderSmsEnabled;
        settings.FormResponderApprovedSmsEnabled = dto.FormResponderApprovedSmsEnabled;
        settings.FormWorkflowRejectedSenderSmsEnabled = dto.FormWorkflowRejectedSenderSmsEnabled;
        settings.FormWorkflowRejectedResponderSmsEnabled = dto.FormWorkflowRejectedResponderSmsEnabled;
        settings.ContractCreatorApprovalNotifySmsEnabled = dto.ContractCreatorApprovalNotifySmsEnabled;
        settings.ContractAmendmentAssigneeSmsEnabled = dto.ContractAmendmentAssigneeSmsEnabled;
        settings.ContractAmendmentReturnToRejecterSmsEnabled = dto.ContractAmendmentReturnToRejecterSmsEnabled;
        settings.ContractRejectionNotifySmsEnabled = dto.ContractRejectionNotifySmsEnabled;
        settings.ContractActionCompletedCreatorSmsEnabled = dto.ContractActionCompletedCreatorSmsEnabled;
        settings.ApprovalReminderSmsEnabled = dto.ApprovalReminderSmsEnabled;
        settings.ApprovalReminderDelayDays = Math.Max(0, dto.ApprovalReminderDelayDays);
        settings.ApprovalReminderDelayHours = Math.Max(0, dto.ApprovalReminderDelayHours);
        if (settings.ApprovalReminderDelayDays == 0 && settings.ApprovalReminderDelayHours == 0)
            settings.ApprovalReminderDelayHours = 24;
        settings.WorkflowValidityReminderSmsEnabled = dto.WorkflowValidityReminderSmsEnabled;
        settings.WorkflowValiditySuspensionDelayDays = Math.Max(0, dto.WorkflowValiditySuspensionDelayDays);
        settings.WorkflowValiditySuspensionDelayHours = Math.Max(0, dto.WorkflowValiditySuspensionDelayHours);
        if (settings.WorkflowValiditySuspensionDelayDays == 0 && settings.WorkflowValiditySuspensionDelayHours == 0)
            settings.WorkflowValiditySuspensionDelayHours = 24;

        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { message = "تنظیمات پیامک ذخیره شد" });
    }
}

