using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Api.Helpers;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Application.Users;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/public/documents")]
[AllowAnonymous]
public class PublicDocumentApprovalsController(
    AppDbContext db,
    IWebHostEnvironment env,
    IDocumentVersionFileAccess files,
    DocumentApprovalLinkService approvalLinks,
    DocumentWorkflowProcessor workflowProcessor) : ControllerBase
{
    [HttpGet("approve")]
    public async Task<IActionResult> ApproveAccess([FromQuery] string c, CancellationToken ct)
    {
        var link = await approvalLinks.ResolveByCodeAsync(c, ct);
        if (link is null) return BadRequest(new { message = "لینک تأیید نامعتبر یا منقضی شده است" });

        var document = link.Document;
        if (document.WorkflowStartedAtUtc is null)
            return BadRequest(new { message = "گردش این سند هنوز شروع نشده است" });

        var steps = DocumentWorkflowProcessor.DeserializeSteps(document.StepsJson);
        if (steps.Count == 0)
            return BadRequest(new { message = "گردش تأیید برای این سند تعریف نشده است" });

        var assigneeId = link.AssigneeUserId;
        var assigneeStep = steps.FirstOrDefault(s => s.UserId == assigneeId);
        if (assigneeStep is null)
            return BadRequest(new { message = "این لینک به تأییدکنندهٔ این سند تعلق ندارد" });

        var current = WorkflowStepJsonHelper.FindCurrentPending(steps, document.CurrentStepOrder);
        var canAct = link.IsActive
            && document.WorkflowStatus == DocumentWorkflowStatus.InProgress
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
            s => $"/api/public/documents/approve/signature?c={Uri.EscapeDataString(c)}&stepOrder={s.Order}");

        var latestVersion = await db.DocumentVersions.AsNoTracking()
            .Where(x => x.DocumentId == document.Id)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);

        var hasFile = latestVersion is not null && files.FileExists(latestVersion);
        var ext = latestVersion?.Extension?.Trim().ToLowerInvariant() ?? "";
        var hasPdf = hasFile && ext == "pdf";

        var assigneeName = assigneeStep.UserName;
        if (string.IsNullOrWhiteSpace(assigneeName) && current?.UserId == assigneeId)
            assigneeName = current.UserName;

        return Ok(new
        {
            document.Id,
            documentTitle = document.Title,
            document.ReferenceNumber,
            document.WorkflowName,
            status = (int)document.WorkflowStatus,
            document.CurrentStepOrder,
            canAct,
            hasFilePreview = hasFile,
            hasPdfPreview = hasPdf,
            viewOnly = !canAct,
            participated,
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
                isCurrent = current is not null && s.Order == current.Order,
            }),
            assignee = new { userId = assigneeId, userName = assigneeName ?? "" },
        });
    }

    [HttpGet("approve/file")]
    public async Task<IActionResult> ApproveFile([FromQuery] string c, CancellationToken ct)
    {
        var link = await approvalLinks.ResolveByCodeAsync(c, ct);
        if (link is null) return BadRequest(new { message = "لینک نامعتبر است" });

        var steps = DocumentWorkflowProcessor.DeserializeSteps(link.Document.StepsJson);
        if (!steps.Any(s => s.UserId == link.AssigneeUserId))
            return Forbid();

        var version = await db.DocumentVersions.AsNoTracking()
            .Where(x => x.DocumentId == link.DocumentId)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (version is null) return NotFound(new { message = "نسخه فایل یافت نشد" });

        var provider = new FileExtensionContentTypeProvider();
        var fileName = version.OriginalFileName ?? $"document-v{version.VersionNumber}";
        if (!provider.TryGetContentType(fileName, out var contentType))
            contentType = "application/octet-stream";
        var served = await DocumentVersionFileHttpHelper.TryServePhysicalAsync(
            files, version, contentType, fileName, inline: true, Response, ct);
        return served ?? NotFound(new { message = "فایل در سرور موجود نیست" });
    }

    [HttpGet("approve/signature")]
    public async Task<IActionResult> GetApproverSignature([FromQuery] string c, [FromQuery] int stepOrder, CancellationToken ct)
    {
        if (stepOrder < 1) return BadRequest(new { message = "stepOrder نامعتبر است" });
        if (string.IsNullOrWhiteSpace(c)) return BadRequest(new { message = "کد لینک الزامی است" });

        var link = await approvalLinks.ResolveByCodeAsync(c, ct);
        if (link is null) return BadRequest(new { message = "لینک نامعتبر است" });

        var steps = DocumentWorkflowProcessor.DeserializeSteps(link.Document.StepsJson);
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
    public async Task<IActionResult> ApproveAction([FromBody] PublicDocumentApproveRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "کد لینک الزامی است" });

        var link = await approvalLinks.ResolveValidAsync(req.Code, ct);
        if (link is null) return BadRequest(new { message = "لینک تأیید نامعتبر یا منقضی شده است" });

        var result = await workflowProcessor.ProcessActionAsync(
            link.DocumentId,
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

    private async Task EnrichStepNamesAsync(List<ApprovalStepDto> steps, CancellationToken ct)
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

public class PublicDocumentApproveRequest
{
    public string Code { get; set; } = "";
    public bool Approve { get; set; }
    public string? Comment { get; set; }
}
