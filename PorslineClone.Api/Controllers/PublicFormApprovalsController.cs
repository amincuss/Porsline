using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.StaticFiles;
using PorslineClone.Application.Contracts;
using PorslineClone.Application.Users;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/public/forms")]
[AllowAnonymous]
public class PublicFormApprovalsController(
    AppDbContext db,
    IWebHostEnvironment env,
    FormSubmissionApprovalLinkService approvalLinks,
    FormWorkflowProcessor workflowProcessor) : ControllerBase
{
    [HttpGet("approve")]
    public async Task<IActionResult> ApproveAccess([FromQuery] string c, CancellationToken ct)
    {
        var link = await approvalLinks.ResolveByCodeAsync(c, ct);
        if (link is null) return BadRequest(new { message = "لینک تأیید نامعتبر یا منقضی شده است" });

        var submission = link.FormSubmission;
        if (submission.Form is null || submission.Form.IsDeleted)
            return BadRequest(new { message = "فرم یافت نشد" });

        if (submission.WorkflowStartedAtUtc is null)
            return BadRequest(new { message = "گردش این پاسخ هنوز شروع نشده است" });

        var steps = FormWorkflowProcessor.DeserializeSteps(submission.StepsJson);
        if (steps.Count == 0)
            return BadRequest(new { message = "گردش تأیید برای این پاسخ تعریف نشده است" });

        var assigneeId = link.AssigneeUserId;
        var assigneeStep = steps.FirstOrDefault(s => s.UserId == assigneeId);
        if (assigneeStep is null)
            return BadRequest(new { message = "این لینک به تأییدکنندهٔ این پاسخ تعلق ندارد" });

        var current = WorkflowStepJsonHelper.FindCurrentPending(steps, submission.CurrentStepOrder);
        var canAct = link.IsActive
            && current is not null
            && current.UserId == assigneeId;

        var participated = assigneeStep.Status is "approved" or "rejected";
        if (!canAct && !participated && (current is null || current.UserId != assigneeId))
            return BadRequest(new { message = "در حال حاضر نوبت تأیید شما نیست" });

        await EnrichStepNamesAsync(steps, ct);
        var approverIds = steps.Select(s => s.UserId).Where(id => id != Guid.Empty).Distinct().ToList();
        if (approverIds.Count > 0)
        {
            var userSigs = await db.Users.AsNoTracking()
                .Where(u => approverIds.Contains(u.Id))
                .Select(u => new { u.Id, u.SignatureImagePath, u.SignatureDisplayDegree })
                .ToDictionaryAsync(
                    u => u.Id,
                    u => (u.SignatureImagePath, u.SignatureDisplayDegree),
                    ct);
            FormApprovalSignatureHelper.BackfillApprovedStepSignatures(steps, userSigs);
        }

        FormApprovalSignatureHelper.EnrichSignatureUrls(
            steps,
            s => $"/api/public/forms/approve/signature?c={Uri.EscapeDataString(c)}&stepOrder={s.Order}");

        var fields = DeserializeFields(submission.FieldsJson);
        var fieldTypesByLabel = await db.FormFields.AsNoTracking()
            .Where(ff => ff.FormId == submission.FormId)
            .GroupBy(ff => ff.Label)
            .Select(g => new { Label = g.Key, FieldType = (int)g.First().FieldType })
            .ToDictionaryAsync(x => x.Label, x => x.FieldType, ct);
        var fileIndices = fields
            .Select((f, i) => (f, i))
            .Where(x => IsUploadPath(x.f.Value))
            .Select(x => x.i)
            .ToList();
        var fileSizes = fileIndices
            .Select(i => ResolveUploadSizeBytes(fields[i].Value))
            .ToList();

        var assigneeName = assigneeStep.UserName;
        if (string.IsNullOrWhiteSpace(assigneeName) && current?.UserId == assigneeId)
            assigneeName = current.UserName;

        return Ok(new
        {
            submission.Id,
            submission.FormId,
            formTitle = submission.Form.Title,
            trackingCode = submission.TrackingCode,
            submission.SubmitterName,
            submission.SubmitterEmail,
            submission.SubmittedAtUtc,
            submission.WorkflowName,
            status = (int)submission.Status,
            submission.CurrentStepOrder,
            canAct,
            viewOnly = !canAct,
            participated,
            fields = fields.Select(f => new
            {
                f.Label,
                f.Value,
                fieldType = fieldTypesByLabel.GetValueOrDefault(f.Label, 0),
            }),
            fileIndices,
            fileSizes,
            steps = steps.Select(s => new
            {
                s.Id,
                s.Order,
                s.UserId,
                s.UserName,
                userFirstName = s.UserFirstName,
                userLastName = s.UserLastName,
                userPositionTitle = s.UserPositionTitle,
                userGender = s.UserGender,
                s.Status,
                s.Comment,
                s.ActionAt,
                s.OnReject,
                s.Note,
                signatureUrl = s.SignatureUrl,
                signatureWidthPx = s.SignatureDisplayDegree is null
                    ? UserSignatureDisplaySize.WidthPxFromDegree(null)
                    : UserSignatureDisplaySize.WidthPxFromDegree(s.SignatureDisplayDegree),
                isCurrent = current is not null && s.Order == current.Order
            }),
            assignee = new
            {
                userId = assigneeId,
                userName = assigneeName ?? ""
            }
        });
    }

    [HttpGet("approve/file")]
    public async Task<IActionResult> ApproveFile([FromQuery] string c, [FromQuery] int index, CancellationToken ct)
    {
        if (index < 0) return BadRequest(new { message = "index نامعتبر است" });

        var link = await approvalLinks.ResolveByCodeAsync(c, ct);
        if (link is null) return BadRequest(new { message = "لینک نامعتبر است" });

        var steps = FormWorkflowProcessor.DeserializeSteps(link.FormSubmission.StepsJson);
        if (!steps.Any(s => s.UserId == link.AssigneeUserId))
            return Forbid();

        var files = FormSubmissionUploadHelper.ListUploadPaths(DeserializeFields(link.FormSubmission.FieldsJson));
        if (index >= files.Count) return NotFound(new { message = "فایل یافت نشد" });

        if (!FormSubmissionUploadHelper.TryResolveDiskPath(env, files[index], out var filePath))
            return NotFound(new { message = "فایل در سرور موجود نیست" });

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(filePath, out var contentType))
            contentType = "application/octet-stream";

        return PhysicalFile(filePath, contentType, Path.GetFileName(filePath), enableRangeProcessing: true);
    }

    [HttpGet("approve/signature")]
    public async Task<IActionResult> GetApproverSignature([FromQuery] string c, [FromQuery] int stepOrder, CancellationToken ct)
    {
        if (stepOrder < 1) return BadRequest(new { message = "stepOrder نامعتبر است" });
        if (string.IsNullOrWhiteSpace(c)) return BadRequest(new { message = "کد لینک الزامی است" });

        var link = await approvalLinks.ResolveByCodeAsync(c, ct);
        if (link is null) return BadRequest(new { message = "لینک نامعتبر است" });

        var steps = FormWorkflowProcessor.DeserializeSteps(link.FormSubmission.StepsJson);
        if (!steps.Any(s => s.UserId == link.AssigneeUserId))
            return Forbid();

        var step = steps.FirstOrDefault(s => s.Order == stepOrder);
        if (step is null || step.Status != "approved" || string.IsNullOrWhiteSpace(step.SignatureImagePath))
            return NotFound(new { message = "امضای این مرحله یافت نشد" });

        if (!FormApprovalSignatureHelper.TryResolveSignatureFile(env, step.SignatureImagePath, out var fullPath))
            return NotFound(new { message = "فایل امضا موجود نیست" });

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(fullPath, out var contentType))
            contentType = "image/png";

        return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
    }

    [HttpPost("approve/action")]
    public async Task<IActionResult> ApproveAction([FromBody] PublicFormApproveRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "کد لینک الزامی است" });

        var link = await approvalLinks.ResolveValidAsync(req.Code, ct);
        if (link is null) return BadRequest(new { message = "لینک تأیید نامعتبر یا منقضی شده است" });

        var result = await workflowProcessor.ProcessActionAsync(
            link.FormSubmissionId,
            link.AssigneeUserId,
            req.Approve,
            req.Comment,
            ct);

        if (!result.Success)
        {
            var status = result.HttpStatus ?? 400;
            return StatusCode(status, new { message = result.Message });
        }

        link.IsActive = false;
        await db.SaveChangesAsync(ct);

        return Ok(new { message = result.Message });
    }

    private static bool IsUploadPath(string? value) => FormSubmissionUploadHelper.IsUploadPath(value);

    private long ResolveUploadSizeBytes(string? uploadPath) =>
        FormSubmissionUploadHelper.ResolveSizeBytes(env, uploadPath);

    private static List<FormFieldValueDto> DeserializeFields(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : (JsonSerializer.Deserialize<List<FormFieldValueDto>>(json) ?? []);

    private async Task EnrichStepNamesAsync(List<PorslineClone.Application.Contracts.ApprovalStepDto> steps, CancellationToken ct)
    {
        var ids = steps.Select(s => s.UserId).Where(id => id != Guid.Empty).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new
            {
                u.Id,
                u.FirstName,
                u.LastName,
                u.UserName,
                u.Gender,
                PositionTitle = u.UserPosition != null ? u.UserPosition.Name : null,
            })
            .ToListAsync(ct);
        var lookup = users.ToDictionary(x => x.Id);
        foreach (var step in steps)
        {
            if (!lookup.TryGetValue(step.UserId, out var u)) continue;
            if (string.IsNullOrWhiteSpace(step.UserName))
            {
                var full = $"{u.FirstName} {u.LastName}".Trim();
                step.UserName = string.IsNullOrWhiteSpace(full) ? u.UserName ?? "" : full;
            }
            FormApprovalSignatureHelper.EnrichApproverIdentityFromProfile(
                step, u.FirstName, u.LastName, u.PositionTitle, u.Gender);
        }
    }
}

public class PublicFormApproveRequest
{
    public string Code { get; set; } = "";
    public bool Approve { get; set; }
    public string? Comment { get; set; }
}
