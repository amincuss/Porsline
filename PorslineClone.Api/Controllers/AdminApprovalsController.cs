using System.Security.Claims;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.StaticFiles;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/approvals")]
[Authorize]
public class AdminApprovalsController(
    AppDbContext db,
    UserManager<AppUser> userManager,
    ISmsSender smsSender,
    IInboxMessageService inbox,
    IWebHostEnvironment env,
    IFrontendUrlResolver frontendUrls,
    FormWorkflowProcessor workflowProcessor) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "approvals.read")]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(currentUserId, out var currentUserGuid);

        var list = await db.FormSubmissions
            .Include(x => x.Form)
            .ApplyVisibleFormSubmissions(db, User)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        var stepsBySubmission = list.ToDictionary(
            x => x.Id,
            x => DeserializeSteps(x.StepsJson));

        var approverIds = stepsBySubmission.Values
            .SelectMany(s => s)
            .Select(s => s.UserId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var approverLookup = await db.Users.AsNoTracking()
            .Where(u => approverIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                u.UserName,
                u.SignatureImagePath,
                u.SignatureDisplayDegree,
                PositionTitle = u.UserPosition != null ? u.UserPosition.Name : null,
            })
            .ToDictionaryAsync(
                x => x.Id,
                x =>
                {
                    var fullName = $"{x.FirstName} {x.LastName}".Trim();
                    var displayName = string.IsNullOrWhiteSpace(fullName) ? x.UserName : fullName;
                    return new
                    {
                        DisplayName = displayName,
                        Email = x.Email,
                        x.SignatureImagePath,
                        x.SignatureDisplayDegree,
                        x.FirstName,
                        x.LastName,
                        x.PositionTitle,
                    };
                },
                ct);

        var userSignatureLookup = approverLookup.ToDictionary(
            x => x.Key,
            x => (x.Value.SignatureImagePath, x.Value.SignatureDisplayDegree));

        foreach (var (submissionId, steps) in stepsBySubmission)
        {
            foreach (var step in steps)
            {
                if (!approverLookup.TryGetValue(step.UserId, out var profile)) continue;
                if (string.IsNullOrWhiteSpace(step.UserName))
                    step.UserName = profile.DisplayName ?? "";
                if (string.IsNullOrWhiteSpace(step.UserEmail))
                    step.UserEmail = profile.Email;
                FormApprovalSignatureHelper.EnrichApproverIdentityFromProfile(
                    step, profile.FirstName, profile.LastName, profile.PositionTitle);
            }
            FormApprovalSignatureHelper.BackfillApprovedStepSignatures(steps, userSignatureLookup);
            var sid = submissionId;
            FormApprovalSignatureHelper.EnrichSignatureUrls(
                steps,
                s => $"/api/admin/approvals/{sid}/signature?stepOrder={s.Order}");
        }

        var result = list.Select(x =>
        {
            var steps = stepsBySubmission[x.Id];
            var fields = DeserializeFields(x.FieldsJson);
            return new ApprovalListItemDto(
                x.Id,
                x.FormId,
                x.Form.Title,
                x.SubmittedAtUtc,
                x.SubmitterName,
                x.SubmitterEmail,
                x.CurrentStepOrder,
                ToClientStatus(x.Status),
                steps,
                fields);
        });

        result = (status ?? "mine").ToLowerInvariant() switch
        {
            "mine" => result.Where(x => x.Steps.Any(s => s.UserId == currentUserGuid && s.Status == "pending")),
            "approved" => result.Where(x => x.OverallStatus == "approved"),
            "rejected" => result.Where(x => x.OverallStatus == "rejected"),
            "all" => result,
            _ => result
        };

        return Ok(result);
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "approvals.update")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] ApprovalActionRequest req, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var currentUserGuid)) return Unauthorized();

        var result = await workflowProcessor.ProcessActionAsync(id, currentUserGuid, true, req.Comment, ct);
        if (!result.Success)
        {
            var code = result.Message?.Contains("امضا", StringComparison.Ordinal) == true
                ? "signature_required"
                : "workflow_action_failed";
            return StatusCode(result.HttpStatus ?? 400, new { message = result.Message, code });
        }
        return Ok(new { message = result.Message });
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "approvals.update")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ApprovalActionRequest req, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var currentUserGuid)) return Unauthorized();

        var result = await workflowProcessor.ProcessActionAsync(id, currentUserGuid, false, req.Comment, ct);
        if (!result.Success)
            return StatusCode(result.HttpStatus ?? 400, new { message = result.Message, code = "workflow_action_failed" });
        return Ok(new { message = result.Message });
    }

    [HttpGet("{id:guid}/signature")]
    [Authorize(Policy = "approvals.read")]
    public async Task<IActionResult> GetStepSignature(Guid id, [FromQuery] int stepOrder, CancellationToken ct)
    {
        if (stepOrder < 1) return BadRequest(new { message = "stepOrder نامعتبر است" });

        var submission = await db.FormSubmissions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (submission is null) return NotFound(new { message = "درخواست یافت نشد" });

        var steps = DeserializeSteps(submission.StepsJson);
        var step = steps.FirstOrDefault(s => s.Order == stepOrder);
        if (step is null || step.Status != "approved" || string.IsNullOrWhiteSpace(step.SignatureImagePath))
            return NotFound(new { message = "امضای این مرحله یافت نشد" });

        if (!FormApprovalSignatureHelper.TryResolveSignatureFile(env, step.SignatureImagePath, out var fullPath))
            return NotFound(new { message = "فایل امضا در سرور موجود نیست" });

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(fullPath, out var contentType))
            contentType = "image/png";

        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
    }

    [HttpGet("{id:guid}/files/{index:int}/download")]
    [Authorize(Policy = "approvals.read")]
    public async Task<IActionResult> DownloadFile(Guid id, int index, CancellationToken ct = default)
    {
        if (index < 0) return BadRequest(new { message = "index نامعتبر است" });

        var submission = await db.FormSubmissions
            .Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == id && x.Form != null && !x.Form.IsDeleted, ct);
        if (submission is null) return NotFound(new { message = "درخواست یافت نشد" });

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("Admin");
        if (!isAdmin)
        {
            if (!Guid.TryParse(currentUserId, out var currentUserGuid))
                return Forbid();

            var steps = DeserializeSteps(submission.StepsJson);
            var isApproverInWorkflow = steps.Any(s => s.UserId == currentUserGuid);
            if (!isApproverInWorkflow)
                return Forbid();
        }

        var values = DeserializeFields(submission.FieldsJson);
        var files = FormSubmissionUploadHelper.ListUploadPaths(values);
        if (index >= files.Count) return NotFound(new { message = "فایل یافت نشد" });

        var url = files[index];
        if (!FormSubmissionUploadHelper.TryResolveDiskPath(env, url, out var filePath))
            return NotFound(new { message = "فایل در سرور موجود نیست" });

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(filePath, out var contentType))
            contentType = "application/octet-stream";

        return PhysicalFile(filePath, contentType, Path.GetFileName(filePath), enableRangeProcessing: true);
    }

    private async Task SendNextAssigneeSmsAsync(
        Guid nextUserId,
        Guid formId,
        string? approverDisplayName,
        string? fallbackApproverName,
        CancellationToken ct)
    {
        var smsSettings = await db.SmsSettings.FirstOrDefaultAsync(ct) ?? new SmsSettings();

        var nextUser = await userManager.FindByIdAsync(nextUserId.ToString());
        if (nextUser is null) return;

        var formTitle = await db.Forms
            .Where(f => f.Id == formId)
            .Select(f => f.Title)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(formTitle)) formTitle = "فرم";

        var senderName = !string.IsNullOrWhiteSpace(approverDisplayName) ? approverDisplayName : fallbackApproverName;
        if (string.IsNullOrWhiteSpace(senderName)) senderName = "کاربر قبلی";

        var msg =
            $"درخواست فرم «{formTitle}» توسط {senderName} تایید شد و برای شما ارجاع گردید.\n" +
            $"لطفا برای بررسی به پنل مدیریت بخش تاییدیه‌ها مراجعه کنید.";
        var adminBase = await frontendUrls.ResolveAdminBaseUrlAsync(ct);
        if (!string.IsNullOrWhiteSpace(adminBase))
            msg += $"\nلینک مستقیم: {adminBase}/admin/approvals";

        await inbox.SendToUserAsync(nextUserId, "ارجاع تأیید فرم", msg, ct);
        if (!smsSettings.ApprovalReferralSmsEnabled || string.IsNullOrWhiteSpace(nextUser.PhoneNumber)) return;
        await smsSender.SendSmsAsync(new PorslineClone.Application.Contracts.SmsRequest(nextUser.PhoneNumber, msg), ct);
    }

    private static List<ApprovalStepDto> DeserializeSteps(string? json)
        => FormWorkflowProcessor.DeserializeSteps(json);

    private static List<FormFieldValueDto> DeserializeFields(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? []
            : (JsonSerializer.Deserialize<List<FormFieldValueDto>>(json) ?? []);

    private static string ToClientStatus(FormSubmissionStatus status) => status switch
    {
        FormSubmissionStatus.Pending => "pending",
        FormSubmissionStatus.InProgress => "in_progress",
        FormSubmissionStatus.Approved => "approved",
        FormSubmissionStatus.Rejected => "rejected",
        FormSubmissionStatus.Submitted => "submitted",
        _ => "pending"
    };
}

public record ApprovalActionRequest(string? Comment);

public record ApprovalListItemDto(
    Guid Id,
    Guid FormId,
    string FormTitle,
    DateTime SubmittedAt,
    string? SubmitterName,
    string? SubmitterEmail,
    int CurrentStep,
    string OverallStatus,
    List<ApprovalStepDto> Steps,
    List<FormFieldValueDto> Fields
);
