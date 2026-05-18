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
[Route("api/public/contracts")]
[AllowAnonymous]
public class PublicContractsController(
    AppDbContext db,
    IWebHostEnvironment env,
    ContractApprovalLinkService approvalLinks,
    ContractWorkflowProcessor workflowProcessor) : ControllerBase
{
    [HttpGet("approve")]
    public async Task<IActionResult> ApproveAccess([FromQuery] string c, CancellationToken ct)
    {
        var link = await approvalLinks.ResolveValidAsync(c, ct);
        if (link is null) return BadRequest(new { message = "لینک تأیید نامعتبر یا منقضی شده است" });

        var contract = link.Contract;
        if (contract.IsArchived)
            return BadRequest(new { message = "این قرارداد بایگانی شده است" });

        var steps = ContractWorkflowProcessor.DeserializeSteps(contract.StepsJson);
        var current = steps.FirstOrDefault(s => s.Order == contract.CurrentStepOrder && s.Status == "pending");
        if (current is null)
            return BadRequest(new { message = "در حال حاضر مرحله‌ای برای تأیید شما فعال نیست" });

        if (current.UserId != link.AssigneeUserId)
            return BadRequest(new { message = "این لینک برای مرحله فعلی معتبر نیست" });

        await EnrichStepNamesAsync(steps, ct);

        var originalPath = await ContractWorkflowProcessor.ResolveOriginalFilePathAsync(contract, db, ct);
        var previewPath = !string.IsNullOrWhiteSpace(contract.FilePath) ? contract.FilePath : originalPath;
        var hasPdf = previewPath?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;

        return Ok(new
        {
            contract.Id,
            contract.ContractNumber,
            contract.Title,
            contract.FirstName,
            contract.LastName,
            contract.NationalId,
            contract.Phone,
            contract.SubjectPersonName,
            contract.DateFromUtc,
            contract.DateToUtc,
            contract.WorkflowName,
            contract.Status,
            contract.CurrentStepOrder,
            canAct = true,
            hasPdfPreview = hasPdf,
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
                isCurrent = s.Order == contract.CurrentStepOrder && s.Status == "pending"
            }),
            assignee = new
            {
                userId = link.AssigneeUserId,
                userName = current.UserName
            }
        });
    }

    [HttpGet("approve/file")]
    public async Task<IActionResult> ApproveFile([FromQuery] string c, CancellationToken ct)
    {
        var link = await approvalLinks.ResolveValidAsync(c, ct);
        if (link is null) return BadRequest(new { message = "لینک نامعتبر است" });

        var contract = link.Contract;
        var steps = ContractWorkflowProcessor.DeserializeSteps(contract.StepsJson);
        var current = steps.FirstOrDefault(s => s.Order == contract.CurrentStepOrder && s.Status == "pending");
        if (current is null || current.UserId != link.AssigneeUserId)
            return Forbid();

        var relative = contract.FilePath;
        if (string.IsNullOrWhiteSpace(relative))
            relative = await ContractWorkflowProcessor.ResolveOriginalFilePathAsync(contract, db, ct);
        if (string.IsNullOrWhiteSpace(relative))
            return NotFound(new { message = "فایل یافت نشد" });

        var relativePath = relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var filePath = Path.Combine(env.ContentRootPath, relativePath);
        if (!System.IO.File.Exists(filePath))
            return NotFound(new { message = "فایل در سرور موجود نیست" });

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(filePath, out var contentType))
            contentType = "application/octet-stream";

        return PhysicalFile(filePath, contentType, enableRangeProcessing: true);
    }

    [HttpPost("approve/action")]
    public async Task<IActionResult> ApproveAction([FromBody] PublicContractApproveRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "کد لینک الزامی است" });

        var link = await approvalLinks.ResolveValidAsync(req.Code, ct);
        if (link is null) return BadRequest(new { message = "لینک تأیید نامعتبر یا منقضی شده است" });

        var result = await workflowProcessor.ProcessActionAsync(
            link.ContractId,
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

public class PublicContractApproveRequest
{
    public string Code { get; set; } = "";
    public bool Approve { get; set; }
    public string? Comment { get; set; }
}
