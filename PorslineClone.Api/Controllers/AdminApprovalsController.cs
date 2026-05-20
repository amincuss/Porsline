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

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/approvals")]
[Authorize]
public class AdminApprovalsController(AppDbContext db, UserManager<AppUser> userManager, ISmsSender smsSender, IInboxMessageService inbox, IWebHostEnvironment env, IFrontendUrlResolver frontendUrls) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "approvals.read")]
    public async Task<IActionResult> List([FromQuery] string? status, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(currentUserId, out var currentUserGuid);

        var query = db.FormSubmissions
            .Include(x => x.Form)
            .AsQueryable();

        var list = await query
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

        var approverLookup = await userManager.Users
            .Where(u => approverIds.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.Email,
                u.UserName
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
                        Email = x.Email
                    };
                },
                ct);

        foreach (var steps in stepsBySubmission.Values)
        {
            foreach (var step in steps)
            {
                if (!approverLookup.TryGetValue(step.UserId, out var profile)) continue;
                if (string.IsNullOrWhiteSpace(step.UserName))
                    step.UserName = profile.DisplayName ?? "";
                if (string.IsNullOrWhiteSpace(step.UserEmail))
                    step.UserEmail = profile.Email;
            }
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

        var submission = await db.FormSubmissions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (submission is null) return NotFound(new { message = "درخواست یافت نشد" });

        var steps = DeserializeSteps(submission.StepsJson);
        var current = steps.FirstOrDefault(s => s.Order == submission.CurrentStepOrder && s.Status == "pending");
        if (current is null) return BadRequest(new { message = "مرحله فعالی برای این درخواست وجود ندارد" });
        if (current.UserId != currentUserGuid) return Forbid();
        var currentUser = await userManager.FindByIdAsync(currentUserGuid.ToString());
        var currentApproverName = currentUser is null
            ? current.UserName
            : $"{currentUser.FirstName} {currentUser.LastName}".Trim();

        current.Status = "approved";
        current.Comment = req.Comment;
        current.ActionAt = DateTime.UtcNow;

        var next = steps.Where(s => s.Order > current.Order).OrderBy(s => s.Order).FirstOrDefault();
        if (next is null)
        {
            submission.Status = FormSubmissionStatus.Approved;
        }
        else
        {
            next.Status = "pending";
            submission.CurrentStepOrder = next.Order;
            submission.Status = FormSubmissionStatus.InProgress;

            await SendNextAssigneeSmsAsync(
                next.UserId,
                submission.FormId,
                currentApproverName,
                current.UserName,
                ct);
        }

        submission.StepsJson = JsonSerializer.Serialize(steps);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "تأیید شد" });
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "approvals.update")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] ApprovalActionRequest req, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var currentUserGuid)) return Unauthorized();

        var submission = await db.FormSubmissions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (submission is null) return NotFound(new { message = "درخواست یافت نشد" });

        var steps = DeserializeSteps(submission.StepsJson);
        var current = steps.FirstOrDefault(s => s.Order == submission.CurrentStepOrder && s.Status == "pending");
        if (current is null) return BadRequest(new { message = "مرحله فعالی برای این درخواست وجود ندارد" });
        if (current.UserId != currentUserGuid) return Forbid();
        var currentUser = await userManager.FindByIdAsync(currentUserGuid.ToString());
        var currentApproverName = currentUser is null
            ? current.UserName
            : $"{currentUser.FirstName} {currentUser.LastName}".Trim();

        current.Status = "rejected";
        current.Comment = req.Comment;
        current.ActionAt = DateTime.UtcNow;

        if (current.OnReject == "continue")
        {
            var next = steps.Where(s => s.Order > current.Order).OrderBy(s => s.Order).FirstOrDefault();
            if (next is null)
            {
                submission.Status = FormSubmissionStatus.Rejected;
            }
            else
            {
                next.Status = "pending";
                submission.CurrentStepOrder = next.Order;
                submission.Status = FormSubmissionStatus.InProgress;

                await SendNextAssigneeSmsAsync(
                    next.UserId,
                    submission.FormId,
                    currentApproverName,
                    current.UserName,
                    ct);
            }
        }
        else
        {
            foreach (var later in steps.Where(s => s.Order > current.Order && s.Status == "waiting"))
                later.Status = "skipped";
            submission.Status = FormSubmissionStatus.Rejected;
        }

        submission.StepsJson = JsonSerializer.Serialize(steps);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "رد شد" });
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
        var files = values
            .Where(x => !string.IsNullOrWhiteSpace(x.Value) && x.Value.StartsWith("/Formupload/", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToList();

        if (index >= files.Count) return NotFound(new { message = "فایل یافت نشد" });

        var url = files[index];
        var relative = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.Combine(env.ContentRootPath, relative);
        if (!System.IO.File.Exists(filePath))
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
        => string.IsNullOrWhiteSpace(json)
            ? []
            : (JsonSerializer.Deserialize<List<ApprovalStepDto>>(json) ?? []);

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
public class ApprovalStepDto
{
    public Guid Id { get; set; }
    public int Order { get; set; }
    public Guid UserId { get; set; }
    public string UserName { get; set; } = "";
    public string? UserEmail { get; set; }
    public string Status { get; set; } = "waiting";
    public string? Comment { get; set; }
    public DateTime? ActionAt { get; set; }
    public string OnReject { get; set; } = "stop";
    public string? Note { get; set; }
}

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
