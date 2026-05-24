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
    ContractWorkflowProcessor workflowProcessor,
    ContractFileStorageService contractFiles) : ControllerBase
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx"
    };

    [HttpGet("approve")]
    public async Task<IActionResult> ApproveAccess([FromQuery] string c, CancellationToken ct)
    {
        var link = await approvalLinks.ResolveByCodeAsync(c, ct);
        if (link is null) return BadRequest(new { message = "لینک تأیید نامعتبر یا منقضی شده است" });

        var contract = link.Contract;
        var isTerminalRejected = contract.Status == ContractStatus.Rejected;
        if (contract.IsArchived && !isTerminalRejected)
            return BadRequest(new { message = "این قرارداد بایگانی شده است" });

        var steps = ContractWorkflowProcessor.DeserializeSteps(contract.StepsJson);
        if (steps.Count == 0)
            return BadRequest(new { message = "گردش تأیید برای این قرارداد تعریف نشده است" });

        var assigneeId = link.AssigneeUserId;
        var amendState = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        var isAmendmentAssignee = ContractAmendmentHelper.IsActive(amendState)
            && amendState!.AssigneeUserId == assigneeId;

        var assigneeStep = steps.FirstOrDefault(s => s.UserId == assigneeId);
        if (assigneeStep is null && !isAmendmentAssignee)
            return BadRequest(new { message = "این لینک به تأییدکنندهٔ این قرارداد تعلق ندارد" });

        var current = steps.FirstOrDefault(s => s.Order == contract.CurrentStepOrder && s.Status == "pending");
        var canAct = link.IsActive
            && !isTerminalRejected
            && !isAmendmentAssignee
            && current is not null
            && current.UserId == assigneeId;

        var participated = assigneeStep?.Status is "approved" or "rejected";
        if (!canAct && !participated && !isTerminalRejected && !isAmendmentAssignee
            && (current is null || current.UserId != assigneeId))
            return BadRequest(new { message = "در حال حاضر نوبت تأیید شما نیست" });

        await EnrichStepNamesAsync(steps, ct);

        var originalPath = await ContractWorkflowProcessor.ResolveOriginalFilePathAsync(contract, db, ct);
        var previewPath = !string.IsNullOrWhiteSpace(contract.FilePath) ? contract.FilePath : originalPath;
        var hasFile = !string.IsNullOrWhiteSpace(previewPath);
        var hasPdf = previewPath?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;

        var assigneeName = assigneeStep?.UserName ?? "";
        if (string.IsNullOrWhiteSpace(assigneeName))
            assigneeName = await ResolveUserDisplayNameAsync(assigneeId, ct);
        if (string.IsNullOrWhiteSpace(assigneeName) && current?.UserId == assigneeId)
            assigneeName = current.UserName ?? "";

        var currentVersionIsAmended = await db.ContractVersions.AsNoTracking()
            .AnyAsync(v => v.ContractId == contract.Id
                           && v.VersionNumber == contract.CurrentVersionNumber
                           && v.IsAmendedVersion, ct);

        var amendment = ContractAmendmentHelper.ToView(amendState, assigneeId);
        var canAmendContract = isAmendmentAssignee && link.IsActive && !isTerminalRejected;
        var overallStatus = ContractAmendmentHelper.IsActive(amendState) ? "amendment" : MapOverallStatus(contract.Status);
        var actionPhase = await ContractActionPhaseHelper.BuildViewAsync(contract, db, ct);

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
            contract.CurrentVersionNumber,
            canAct,
            hasPdfPreview = hasPdf,
            hasFilePreview = hasFile,
            viewOnly = !canAct && !canAmendContract,
            participated,
            isTerminalRejected,
            currentVersionIsAmended,
            overallStatus,
            canAmendContract,
            amendmentAssigneeName = assigneeName,
            amendment,
            actionPhase,
            workflowEvents = WorkflowEventHelper.ToViews(contract.WorkflowEventsJson),
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
                s.RejectionType,
                s.ReviewCycle,
                s.LastRejectionComment,
                s.LastRejectionType,
                s.LastRejectedAtUtc,
                isCurrent = s.Order == contract.CurrentStepOrder && s.Status == "pending"
            }),
            assignee = new
            {
                userId = assigneeId,
                userName = assigneeName ?? ""
            }
        });
    }

    [HttpGet("approve/file")]
    public async Task<IActionResult> ApproveFile([FromQuery] string c, CancellationToken ct)
    {
        var link = await approvalLinks.ResolveByCodeAsync(c, ct);
        if (link is null) return BadRequest(new { message = "لینک نامعتبر است" });

        var contract = link.Contract;
        var steps = ContractWorkflowProcessor.DeserializeSteps(contract.StepsJson);
        var amendState = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        var isAmendmentAssignee = ContractAmendmentHelper.IsActive(amendState)
            && amendState!.AssigneeUserId == link.AssigneeUserId;
        if (!steps.Any(s => s.UserId == link.AssigneeUserId) && !isAmendmentAssignee)
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

        if (link.Contract.Status == ContractStatus.Rejected)
            return BadRequest(new { message = "گردش با رد قطعی پایان یافته است" });

        var amendState = ContractAmendmentHelper.Deserialize(link.Contract.AmendmentJson);
        if (ContractAmendmentHelper.IsActive(amendState) && amendState!.AssigneeUserId == link.AssigneeUserId)
            return BadRequest(new { message = "قرارداد در فاز اصلاحیه است. از دکمه «اصلاح قرارداد» استفاده کنید." });

        var result = await workflowProcessor.ProcessActionAsync(
            link.ContractId,
            link.AssigneeUserId,
            req.Approve,
            req.Comment,
            req.RejectionType,
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

    [HttpPost("approve/amendment-file")]
    [RequestSizeLimit(25 * 1024 * 1024)]
    public async Task<IActionResult> UploadAmendmentFile([FromQuery] string c, IFormFile? file, CancellationToken ct)
    {
        var link = await approvalLinks.ResolveByCodeAsync(c, ct);
        if (link is null || !link.IsActive)
            return BadRequest(new { message = "لینک نامعتبر یا منقضی شده است" });

        var contract = link.Contract;
        if (contract.IsArchived)
            return BadRequest(new { message = "قرارداد بایگانی‌شده قابل ویرایش نیست" });

        if (file is null || file.Length == 0)
            return BadRequest(new { message = "فایل الزامی است" });

        var ext = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(ext))
            return BadRequest(new { message = "فقط فایل PDF یا Word مجاز است" });
        if (file.Length > 20 * 1024 * 1024)
            return BadRequest(new { message = "حداکثر حجم فایل ۲۰ مگابایت است" });

        var amendState = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        if (!ContractAmendmentHelper.IsActive(amendState) || amendState!.Phase != "creator_amendment")
            return BadRequest(new { message = "آپلود نسخه اصلاح‌شده فقط در فاز اصلاحیه ایجادکننده مجاز است" });
        if (amendState.AssigneeUserId != link.AssigneeUserId)
            return Forbid();

        var userId = link.AssigneeUserId;
        var nextVersion = contract.CurrentVersionNumber + 1;
        var uploaderName = await ResolveUserDisplayNameAsync(userId, ct);
        var stored = await contractFiles.SaveAsync(contract.NationalId, nextVersion, contract.ContractNumber, file, ct);

        contract.FilePath = stored.relativePath;
        contract.OriginalFilePath = stored.relativePath;
        contract.FileName = stored.originalFileName;
        contract.PdfFilePath = null;
        contract.CurrentVersionNumber = nextVersion;

        db.ContractVersions.Add(new ContractVersion
        {
            Id = Guid.NewGuid(),
            ContractId = contract.Id,
            VersionNumber = nextVersion,
            FilePath = stored.relativePath,
            FileName = stored.originalFileName,
            CreatedByUserId = userId,
            CreatedByName = uploaderName,
            CreatedAtUtc = DateTime.UtcNow,
            ChangeNote = "نسخه اصلاح‌شده",
            IsAmendedVersion = true
        });
        await db.SaveChangesAsync(ct);

        var result = await workflowProcessor.RegisterAmendedVersionAsync(contract.Id, userId, nextVersion, ct);
        if (!result.Success)
            return BadRequest(new { message = result.Message });

        return Ok(new
        {
            versionNumber = nextVersion,
            currentVersionIsAmended = true,
            message = result.Message
        });
    }

    [HttpPost("approve/amendment")]
    public async Task<IActionResult> UpdateAmendment([FromBody] PublicContractAmendmentRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Code))
            return BadRequest(new { message = "کد لینک الزامی است" });

        var link = await approvalLinks.ResolveByCodeAsync(req.Code, ct);
        if (link is null || !link.IsActive)
            return BadRequest(new { message = "لینک نامعتبر یا منقضی شده است" });

        var result = await workflowProcessor.UpdateAmendmentAsync(
            link.ContractId,
            link.AssigneeUserId,
            req.AmendmentStatus,
            req.Note,
            ct);

        if (!result.Success)
        {
            var status = result.HttpStatus ?? 400;
            if (status == 403) return StatusCode(403, new { message = result.Message });
            return BadRequest(new { message = result.Message });
        }

        return Ok(new { message = result.Message });
    }

    private static string MapOverallStatus(ContractStatus status) => status switch
    {
        ContractStatus.InProgress => "in_progress",
        ContractStatus.Approved => "approved",
        ContractStatus.Rejected => "rejected",
        ContractStatus.Suspended => "suspended",
        ContractStatus.Incomplete => "incomplete",
        _ => "pending"
    };

    private async Task<string> ResolveUserDisplayNameAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return "";
        var full = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? (user.UserName ?? "") : full;
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
    /// <summary>full | contract_amendment | needs_meeting — هنگام رد</summary>
    public string? RejectionType { get; set; }
}

public class PublicContractAmendmentRequest
{
    public string Code { get; set; } = "";
    public string AmendmentStatus { get; set; } = "waiting";
    public string? Note { get; set; }
}
