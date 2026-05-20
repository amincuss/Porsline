using System.Security.Claims;
using System.IO;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
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
[Route("api/admin/user-forms")]
[Authorize]
public class AdminUserFormsController(
    AppDbContext db,
    IFrontendUrlResolver frontendUrls,
    IWebHostEnvironment env,
    FormWorkflowProcessor workflowProcessor) : ControllerBase
{
    private async Task<FormSubmission?> GetAuthorizedSubmissionAsync(Guid id, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("Admin");
        var submission = await db.FormSubmissions
            .Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == id && x.Form != null && !x.Form.IsDeleted, ct);
        if (submission is null) return null;
        if (!isAdmin && !string.IsNullOrWhiteSpace(currentUserId) && submission.Form.UserId != currentUserId) return null;
        return submission;
    }

    [HttpGet]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = "submitted_desc",
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(currentUserId, out var currentUserGuid);
        var isAdmin = User.IsInRole("Admin");

        var q = db.FormSubmissions
            .Include(x => x.Form)
            .Where(x => x.Form != null && !x.Form.IsDeleted)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                (x.SubmitterName ?? "").Contains(s) ||
                (x.SubmitterEmail ?? "").Contains(s) ||
                x.Form.Title.Contains(s));
        }

        if (!isAdmin && !string.IsNullOrWhiteSpace(currentUserId))
            q = q.Where(x => x.Form.UserId == currentUserId);

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim().ToLowerInvariant();
            q = st switch
            {
                "approved" => q.Where(x => x.Status == FormSubmissionStatus.Approved),
                "rejected" => q.Where(x => x.Status == FormSubmissionStatus.Rejected),
                "in_progress" => q.Where(x => x.Status == FormSubmissionStatus.InProgress),
                "pending" => q.Where(x => x.Status == FormSubmissionStatus.Pending),
                "submitted" => q.Where(x => x.Status == FormSubmissionStatus.Submitted),
                _ => q
            };
        }

        q = sortBy switch
        {
            "submitted_asc" => q.OrderBy(x => x.SubmittedAtUtc),
            "name_asc" => q.OrderBy(x => x.SubmitterName),
            "name_desc" => q.OrderByDescending(x => x.SubmitterName),
            _ => q.OrderByDescending(x => x.SubmittedAtUtc)
        };

        await ProcessDueScheduledWorkflowStartsAsync(ct);

        var total = await q.CountAsync(ct);
        var data = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var baseUrl = (await frontendUrls.ResolvePublicBaseUrlAsync(ct)) ?? "";
        var result = new List<object>();
        foreach (var x in data)
        {
            var steps = string.IsNullOrWhiteSpace(x.StepsJson)
                ? new List<ApprovalStepDto>()
                : (JsonSerializer.Deserialize<List<ApprovalStepDto>>(x.StepsJson) ?? new List<ApprovalStepDto>());
            var latest = steps
                .Where(s => s.Status == "approved" || s.Status == "rejected")
                .OrderByDescending(s => s.ActionAt ?? DateTime.MinValue)
                .FirstOrDefault();
            string? accessCode = null;
            if (x.DispatchLinkId is not null)
            {
                accessCode = await db.FormDispatchLinks
                    .Where(l => l.Id == x.DispatchLinkId)
                    .Select(l => l.Code)
                    .FirstOrDefaultAsync(ct);
            }
            else if (x.ResponderId is not null)
            {
                accessCode = await db.FormDispatchLinks
                    .Where(l => l.FormId == x.FormId && l.ResponderId == x.ResponderId && l.UsedAtUtc != null)
                    .OrderByDescending(l => l.UsedAtUtc)
                    .Select(l => l.Code)
                    .FirstOrDefaultAsync(ct);
            }

            result.Add(new
            {
                x.Id,
                x.FormId,
                FormTitle = x.Form.Title,
                x.SubmittedAtUtc,
                SubmitterName = x.SubmitterName,
                SubmitterMobile = x.SubmitterEmail,
                ApprovalStatus = ToClientStatus(x.Status),
                SuggestedWorkflowTemplateId = x.Form.WorkflowTemplateId,
                SuggestedWorkflowName = x.Form.WorkflowName,
                LatestApprover = latest?.UserName,
                LatestApproverActionAt = latest?.ActionAt,
                IsApprovalCompleted = x.Status is FormSubmissionStatus.Approved or FormSubmissionStatus.Rejected,
                PublicLink = string.IsNullOrWhiteSpace(accessCode) ? null : $"{baseUrl}/forms/fill?c={accessCode}",
                x.WorkflowName,
                x.WorkflowTemplateId,
                x.WorkflowStartedAtUtc,
                x.WorkflowScheduledStartAtUtc,
                CanStartWorkflow = CanStartWorkflow(x),
                CanAssignWorkflow = CanAssignWorkflow(x),
                CanUnassignWorkflow = CanUnassignWorkflow(x),
                NeedsWorkflowStart = x.Status == FormSubmissionStatus.Pending && x.WorkflowTemplateId is not null,
            });
        }

        return Ok(new
        {
            items = result,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct = default)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });

        var values = string.IsNullOrWhiteSpace(submission.FieldsJson)
            ? new List<FormFieldValueDto>()
            : (JsonSerializer.Deserialize<List<FormFieldValueDto>>(submission.FieldsJson) ?? new List<FormFieldValueDto>());
        var fileValues = values
            .Where(x => !string.IsNullOrWhiteSpace(x.Value) && x.Value.StartsWith("/Formupload/", StringComparison.OrdinalIgnoreCase))
            .Select((x, i) =>
            {
                var url = x.Value;
                var relative = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
                var filePath = Path.Combine(env.ContentRootPath, relative);
                var fileInfo = new FileInfo(filePath);
                var ext = Path.GetExtension(url).ToLowerInvariant();
                var kind = ext switch
                {
                    ".pdf" => "pdf",
                    ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "image",
                    _ => "file"
                };
                return new
                {
                    Index = i,
                    x.Label,
                    Url = url,
                    FileName = Path.GetFileName(url),
                    SizeBytes = fileInfo.Exists ? fileInfo.Length : 0L,
                    Kind = kind,
                    DownloadUrl = $"/api/admin/user-forms/{submission.Id}/files/{i}/download"
                };
            })
            .ToList();

        var steps = string.IsNullOrWhiteSpace(submission.StepsJson)
            ? new List<ApprovalStepDto>()
            : (JsonSerializer.Deserialize<List<ApprovalStepDto>>(submission.StepsJson) ?? new List<ApprovalStepDto>());

        return Ok(new
        {
            submission.Id,
            submission.FormId,
            FormTitle = submission.Form.Title,
            submission.SubmittedAtUtc,
            SubmitterName = submission.SubmitterName,
            SubmitterMobile = submission.SubmitterEmail,
            ApprovalStatus = ToClientStatus(submission.Status),
            SuggestedWorkflowTemplateId = submission.Form.WorkflowTemplateId,
            SuggestedWorkflowName = submission.Form.WorkflowName,
            Fields = values.Select(v => new
            {
                v.Label,
                v.Value,
                IsFile = !string.IsNullOrWhiteSpace(v.Value) && v.Value.StartsWith("/Formupload/", StringComparison.OrdinalIgnoreCase),
                File = fileValues.FirstOrDefault(f => f.Url == v.Value)
            }),
            Files = fileValues,
            Steps = steps.Select(s => new
            {
                s.Order,
                s.UserName,
                s.Status,
                s.ActionAt,
                s.Note
            }),
            submission.WorkflowName,
            submission.WorkflowTemplateId,
            submission.WorkflowStartedAtUtc,
            submission.WorkflowScheduledStartAtUtc,
            CanStartWorkflow = CanStartWorkflow(submission),
            CanAssignWorkflow = CanAssignWorkflow(submission),
            CanUnassignWorkflow = CanUnassignWorkflow(submission),
        });
    }

    [HttpPost("{id:guid}/assign-workflow")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> AssignWorkflow(Guid id, [FromBody] AssignWorkflowRequest req, CancellationToken ct)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });
        if (!CanAssignWorkflow(submission))
            return BadRequest(new { message = "در وضعیت فعلی امکان انتصاب گردش وجود ندارد" });

        if (!Guid.TryParse(req.WorkflowTemplateId, out var templateId))
            return BadRequest(new { message = "گردش انتخاب‌شده نامعتبر است" });

        var template = await db.FormWorkflowTemplates
            .FirstOrDefaultAsync(x => x.Id == templateId && x.IsActive, ct);
        if (template is null)
            return BadRequest(new { message = "گردش یافت نشد یا غیرفعال است" });

        var mode = (req.StartMode ?? "manual").Trim().ToLowerInvariant();
        DateTime? scheduledUtc = null;
        if (mode == "scheduled")
        {
            if (string.IsNullOrWhiteSpace(req.ScheduledStartAtUtc) ||
                !DateTime.TryParse(req.ScheduledStartAtUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                return BadRequest(new { message = "تاریخ شروع گردش نامعتبر است" });
            scheduledUtc = parsed.Kind == DateTimeKind.Unspecified
                ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc)
                : parsed.ToUniversalTime();
            if (scheduledUtc <= DateTime.UtcNow)
                return BadRequest(new { message = "تاریخ شروع باید در آینده باشد" });
        }

        submission.WorkflowTemplateId = template.Id;
        submission.WorkflowName = template.Name;
        submission.WorkflowStartedAtUtc = null;
        submission.WorkflowScheduledStartAtUtc = mode == "scheduled" ? scheduledUtc : null;
        submission.Status = FormSubmissionStatus.Pending;
        submission.CurrentStepOrder = 1;
        submission.StepsJson = JsonSerializer.Serialize(
            WorkflowStepBuilder.BuildApprovalStepsFromTemplate(template.StepsJson, startImmediately: false));

        await db.SaveChangesAsync(ct);

        if (mode == "now")
        {
            await db.Entry(submission).ReloadAsync(ct);
            var (ok, err) = await workflowProcessor.TryStartWorkflowAsync(submission, ct);
            if (!ok) return BadRequest(new { message = err ?? "شروع گردش ناموفق بود" });
            return Ok(new
            {
                message = $"گردش «{submission.WorkflowName}» انتصاب و شروع شد",
                workflowStartedAtUtc = submission.WorkflowStartedAtUtc,
            });
        }

        if (mode == "scheduled")
        {
            return Ok(new
            {
                message = $"گردش «{submission.WorkflowName}» انتصاب شد و در تاریخ برنامه‌ریزی‌شده شروع می‌شود",
                workflowScheduledStartAtUtc = submission.WorkflowScheduledStartAtUtc,
            });
        }

        return Ok(new
        {
            message = $"گردش «{submission.WorkflowName}» انتصاب شد. برای شروع دکمه «شروع گردش» را بزنید",
            canStartWorkflow = true,
        });
    }

    [HttpPost("{id:guid}/unassign-workflow")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> UnassignWorkflow(Guid id, CancellationToken ct)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });
        if (!CanUnassignWorkflow(submission))
            return BadRequest(new { message = "در وضعیت فعلی امکان حذف گردش وجود ندارد" });

        submission.WorkflowTemplateId = null;
        submission.WorkflowName = null;
        submission.WorkflowStartedAtUtc = null;
        submission.WorkflowScheduledStartAtUtc = null;
        submission.StepsJson = null;
        submission.Status = FormSubmissionStatus.Submitted;
        submission.CurrentStepOrder = 0;

        var links = await db.FormSubmissionApprovalLinks
            .Where(x => x.FormSubmissionId == id && x.IsActive)
            .ToListAsync(ct);
        foreach (var link in links)
            link.IsActive = false;

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گردش از پاسخ فرم حذف شد" });
    }

    [HttpPost("{id:guid}/start-workflow")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> StartWorkflow(Guid id, CancellationToken ct)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });
        if (submission.WorkflowTemplateId is null && string.IsNullOrWhiteSpace(submission.StepsJson))
            return BadRequest(new { message = "برای این پاسخ گردش تأیید تعریف نشده است" });
        if (submission.WorkflowStartedAtUtc is not null)
            return BadRequest(new { message = "گردش این پاسخ قبلاً شروع شده است" });
        if (submission.Status != FormSubmissionStatus.Pending)
            return BadRequest(new { message = "گردش این پاسخ قبلاً شروع شده یا به پایان رسیده است" });

        var (ok, err) = await workflowProcessor.TryStartWorkflowAsync(submission, ct);
        if (!ok) return BadRequest(new { message = err ?? "شروع گردش ناموفق بود" });

        return Ok(new { message = $"گردش «{submission.WorkflowName ?? "تأیید"}» شروع شد" });
    }

    [HttpPost("{id:guid}/resend-approval-sms")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> ResendApprovalSms(Guid id, CancellationToken ct)
    {
        var result = await workflowProcessor.ResendPendingApprovalSmsAsync(id, ct);
        if (!result.Success)
        {
            var status = result.HttpStatus ?? 400;
            if (status == 404) return NotFound(new { message = result.Message });
            return BadRequest(new { message = result.Message });
        }
        return Ok(new { message = result.Message });
    }

    private async Task ProcessDueScheduledWorkflowStartsAsync(CancellationToken ct)
    {
        var due = await db.FormSubmissions
            .Where(x => x.WorkflowScheduledStartAtUtc != null
                && x.WorkflowScheduledStartAtUtc <= DateTime.UtcNow
                && x.WorkflowStartedAtUtc == null
                && x.WorkflowTemplateId != null
                && x.Status == FormSubmissionStatus.Pending)
            .ToListAsync(ct);

        foreach (var submission in due)
            await workflowProcessor.TryStartWorkflowAsync(submission, ct);
    }

    private static bool HasAssignedWorkflow(FormSubmission submission) =>
        submission.WorkflowTemplateId is not null
        || (!string.IsNullOrWhiteSpace(submission.StepsJson) && submission.StepsJson.Trim() != "[]");

    private static bool CanAssignWorkflow(FormSubmission submission) =>
        submission.WorkflowStartedAtUtc is null
        && !HasAssignedWorkflow(submission)
        && submission.Status is FormSubmissionStatus.Submitted
            or FormSubmissionStatus.Approved; // پاسخ‌های قدیمی که بدون گردش «تأیید» شده بودند

    private static bool CanUnassignWorkflow(FormSubmission submission) =>
        submission.WorkflowStartedAtUtc is null
        && submission.Status is not FormSubmissionStatus.InProgress
        && HasAssignedWorkflow(submission);

    private static bool CanStartWorkflow(FormSubmission submission) =>
        submission.Status == FormSubmissionStatus.Pending
        && submission.WorkflowTemplateId is not null
        && submission.WorkflowStartedAtUtc is null
        && !string.IsNullOrWhiteSpace(submission.StepsJson)
        && (submission.WorkflowScheduledStartAtUtc is null || submission.WorkflowScheduledStartAtUtc <= DateTime.UtcNow);

    private static string ToClientStatus(FormSubmissionStatus status) => status switch
    {
        FormSubmissionStatus.Pending => "pending",
        FormSubmissionStatus.InProgress => "in_progress",
        FormSubmissionStatus.Approved => "approved",
        FormSubmissionStatus.Rejected => "rejected",
        FormSubmissionStatus.Submitted => "submitted",
        _ => "pending"
    };

    [HttpGet("{id:guid}/files/{index:int}/download")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> DownloadFile(Guid id, int index, CancellationToken ct = default)
    {
        if (index < 0) return BadRequest(new { message = "index نامعتبر است" });
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });

        var values = string.IsNullOrWhiteSpace(submission.FieldsJson)
            ? new List<FormFieldValueDto>()
            : (JsonSerializer.Deserialize<List<FormFieldValueDto>>(submission.FieldsJson) ?? new List<FormFieldValueDto>());
        var files = values
            .Where(x => !string.IsNullOrWhiteSpace(x.Value) && x.Value.StartsWith("/Formupload/", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Value)
            .ToList();

        if (index >= files.Count) return NotFound(new { message = "فایل یافت نشد" });
        var url = files[index];
        var relative = url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.Combine(env.ContentRootPath, relative);
        if (!System.IO.File.Exists(filePath)) return NotFound(new { message = "فایل در سرور موجود نیست" });

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(filePath, out var contentType))
            contentType = "application/octet-stream";

        return PhysicalFile(filePath, contentType, Path.GetFileName(filePath), enableRangeProcessing: true);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "responders.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserFormRequest req, CancellationToken ct = default)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });

        submission.SubmitterName = req.SubmitterName?.Trim();
        submission.SubmitterEmail = req.SubmitterMobile?.Trim();

        var existingValues = string.IsNullOrWhiteSpace(submission.FieldsJson)
            ? new List<FormFieldValueDto>()
            : (JsonSerializer.Deserialize<List<FormFieldValueDto>>(submission.FieldsJson) ?? new List<FormFieldValueDto>());

        if (req.Fields is { Count: > 0 })
        {
            var byLabel = req.Fields
                .Where(x => !string.IsNullOrWhiteSpace(x.Label))
                .GroupBy(x => x.Label.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Last().Value ?? "");

            var updatedValues = existingValues.Select(f =>
            {
                if (!byLabel.TryGetValue(f.Label, out var newValue))
                    return f;
                // Keep uploaded file references untouched here.
                if (!string.IsNullOrWhiteSpace(f.Value) && f.Value.StartsWith("/Formupload/", StringComparison.OrdinalIgnoreCase))
                    return f;
                return new FormFieldValueDto(f.Label, newValue);
            }).ToList();
            submission.FieldsJson = JsonSerializer.Serialize(updatedValues);
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "پاسخ فرم بروزرسانی شد" });
    }
}

public record UpdateUserFormFieldRequest(string Label, string? Value);
public record UpdateUserFormRequest(string? SubmitterName, string? SubmitterMobile, List<UpdateUserFormFieldRequest>? Fields);

