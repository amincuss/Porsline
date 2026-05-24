using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
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

        var current = steps.FirstOrDefault(s => s.Order == submission.CurrentStepOrder && s.Status == "pending");
        var canAct = link.IsActive
            && current is not null
            && current.UserId == assigneeId;

        var participated = assigneeStep.Status is "approved" or "rejected";
        if (!canAct && !participated && (current is null || current.UserId != assigneeId))
            return BadRequest(new { message = "در حال حاضر نوبت تأیید شما نیست" });

        await EnrichStepNamesAsync(steps, ct);

        var fields = DeserializeFields(submission.FieldsJson);
        var fileIndices = fields
            .Select((f, i) => (f, i))
            .Where(x => IsUploadPath(x.f.Value))
            .Select(x => x.i)
            .ToList();

        var assigneeName = assigneeStep.UserName;
        if (string.IsNullOrWhiteSpace(assigneeName) && current?.UserId == assigneeId)
            assigneeName = current.UserName;

        return Ok(new
        {
            submission.Id,
            submission.FormId,
            formTitle = submission.Form.Title,
            submission.SubmitterName,
            submission.SubmitterEmail,
            submission.SubmittedAtUtc,
            submission.WorkflowName,
            status = (int)submission.Status,
            submission.CurrentStepOrder,
            canAct,
            viewOnly = !canAct,
            participated,
            fields,
            fileIndices,
            steps = steps.Select(s => new
            {
                s.Id,
                s.Order,
                s.UserId,
                s.UserName,
                s.Status,
                s.Comment,
                s.ActionAt,
                s.OnReject,
                s.Note,
                isCurrent = s.Order == submission.CurrentStepOrder && s.Status == "pending"
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

        var files = DeserializeFields(link.FormSubmission.FieldsJson)
            .Where(x => IsUploadPath(x.Value))
            .Select(x => x.Value)
            .ToList();

        if (index >= files.Count) return NotFound(new { message = "فایل یافت نشد" });

        var relative = files[index].TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.Combine(env.ContentRootPath, relative);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { message = "فایل در سرور موجود نیست" });

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(filePath, out var contentType))
            contentType = "application/octet-stream";

        return PhysicalFile(filePath, contentType, Path.GetFileName(filePath), enableRangeProcessing: true);
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

    private static bool IsUploadPath(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.StartsWith("/Formupload/", StringComparison.OrdinalIgnoreCase);

    private static List<FormFieldValueDto> DeserializeFields(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : (JsonSerializer.Deserialize<List<FormFieldValueDto>>(json) ?? []);

    private async Task EnrichStepNamesAsync(List<PorslineClone.Application.Contracts.ApprovalStepDto> steps, CancellationToken ct)
    {
        var ids = steps.Select(s => s.UserId).Where(id => id != Guid.Empty).Distinct().ToList();
        var users = await db.Users
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })
            .ToListAsync(ct);
        var lookup = users.ToDictionary(
            x => x.Id,
            x =>
            {
                var full = $"{x.FirstName} {x.LastName}".Trim();
                return string.IsNullOrWhiteSpace(full) ? x.UserName ?? "" : full;
            });
        foreach (var step in steps)
        {
            if (!string.IsNullOrWhiteSpace(step.UserName)) continue;
            if (lookup.TryGetValue(step.UserId, out var name))
                step.UserName = name;
        }
    }
}

public class PublicFormApproveRequest
{
    public string Code { get; set; } = "";
    public bool Approve { get; set; }
    public string? Comment { get; set; }
}
