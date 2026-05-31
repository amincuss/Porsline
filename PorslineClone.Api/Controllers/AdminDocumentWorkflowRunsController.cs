using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/document-workflow-runs")]
[Authorize]
public class AdminDocumentWorkflowRunsController(
    AppDbContext db,
    IWebHostEnvironment env,
    DocumentWorkflowProcessor workflowProcessor,
    DocumentPostApprovalService postApproval) : ControllerBase
{
    private bool IsAdmin => User.IsInRole("Admin");

    private static bool UserIsInDocumentWorkflow(Document document, Guid userId)
    {
        var steps = DocumentWorkflowProcessor.DeserializeSteps(document.StepsJson);
        return steps.Any(s => s.UserId == userId);
    }

    private bool CanReadWorkflowRuns =>
        IsAdmin
        || User.HasClaim("permission", "documents.workflow.read")
        || User.HasClaim("permission", "forms.read");

    private static bool HasWorkflowRun(Document document) =>
        document.WorkflowTemplateId is not null
        || document.WorkflowStartedAtUtc is not null
        || (!string.IsNullOrWhiteSpace(document.StepsJson) && document.StepsJson.Trim() != "[]");

    private async Task<bool> UserCanAccessDocumentAsync(Document document, Guid userId, CancellationToken ct)
    {
        if (IsAdmin) return true;
        if (UserIsInDocumentWorkflow(document, userId)) return true;
        if (document.OwnerUserId == userId) return true;
        return false;
    }

    private IQueryable<Document> ScopeVisibleDocuments(IQueryable<Document> query, Guid userId)
    {
        if (IsAdmin) return query;
        return query.Where(x =>
            x.OwnerUserId == userId
            || (x.StepsJson != null && x.StepsJson.Contains(userId.ToString())));
    }

    private async Task<Document?> GetAuthorizedDocumentAsync(Guid id, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var userGuid))
            return null;

        var document = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (document is null || !HasWorkflowRun(document))
            return null;

        if (!CanReadWorkflowRuns)
            return null;

        if (!await UserCanAccessDocumentAsync(document, userGuid, ct))
            return null;

        return document;
    }

    [HttpGet]
    [Authorize(Policy = "documents.workflow.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] bool awaitingMe = false,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var currentUserGuid))
            return Unauthorized();

        var q = db.Documents
            .Where(x => x.WorkflowTemplateId != null || x.WorkflowStartedAtUtc != null);

        q = ScopeVisibleDocuments(q, currentUserGuid);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                x.Title.Contains(s)
                || (x.ReferenceNumber ?? "").Contains(s)
                || (x.WorkflowName ?? "").Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim().ToLowerInvariant();
            q = st switch
            {
                "approved" => q.Where(x => x.WorkflowStatus == DocumentWorkflowStatus.Approved),
                "rejected" => q.Where(x => x.WorkflowStatus == DocumentWorkflowStatus.Rejected),
                "in_progress" => q.Where(x => x.WorkflowStatus == DocumentWorkflowStatus.InProgress),
                "pending" => q.Where(x => x.WorkflowStatus == DocumentWorkflowStatus.Pending),
                _ => q
            };
        }

        List<Document> data;
        int total;
        if (awaitingMe)
        {
            (data, total) = await PaginateAwaitingMeAsync(q, currentUserGuid, page, pageSize, ct);
        }
        else
        {
            total = await q.CountAsync(ct);
            data = await q
                .OrderByDescending(x => x.WorkflowStartedAtUtc ?? x.UpdatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);
        }

        var items = data.Select(x =>
        {
            var steps = DeserializeSteps(x.StepsJson);
            var approved = steps.Count(s => string.Equals(s.Status, "approved", StringComparison.OrdinalIgnoreCase));
            var isAwaitingMyApproval = IsAwaitingUserApproval(x, steps, currentUserGuid);
            var isAwaitingMyAction = DocumentActionPhaseHelper.IsAwaitingUserAction(x, currentUserGuid);
            var actionState = PostApprovalJsonHelper.DeserializeState(x.PostApprovalJson);
            return new
            {
                x.Id,
                DocumentId = x.Id,
                DocumentTitle = x.Title,
                x.ReferenceNumber,
                x.WorkflowStartedAtUtc,
                x.WorkflowScheduledStartAtUtc,
                ApprovalStatus = ToClientStatus(x.WorkflowStatus),
                x.WorkflowName,
                StepCount = steps.Count,
                ApprovedStepCount = approved,
                x.CurrentStepOrder,
                IsAwaitingMyApproval = isAwaitingMyApproval,
                IsAwaitingMyAction = isAwaitingMyAction,
                ListTag = ResolveListTag(x, isAwaitingMyApproval, isAwaitingMyAction),
                HasActionPhase = actionState is { AssigneeUserIds.Count: > 0 },
                ActionDirectionLabel = actionState?.ActionDirectionLabel,
                ActionPhaseStatus = actionState?.Status,
                ActionPhaseStatusLabel = actionState is null
                    ? null
                    : PostApprovalJsonHelper.StatusLabel(actionState.Status),
                x.WorkflowRunCycle,
                IsWorkflowRerun = x.WorkflowRunCycle > 1,
            };
        });

        return Ok(new
        {
            items,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling((double)total / pageSize)
        });
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "documents.workflow.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var document = await GetAuthorizedDocumentAsync(id, ct);
        if (document is null) return NotFound(new { message = "گردش سند یافت نشد" });

        var steps = DeserializeSteps(document.StepsJson);
        await EnrichStepsAsync(document.Id, steps, ct);

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(currentUserId, out var currentUserGuid);
        var canAct = IsAwaitingUserApproval(document, steps, currentUserGuid);
        var isAwaitingMyAction = DocumentActionPhaseHelper.IsAwaitingUserAction(document, currentUserGuid);
        var actionPhase = await DocumentActionPhaseHelper.BuildViewAsync(document, db, ct);
        var canUpdateAction = isAwaitingMyAction
            && (User.HasClaim("permission", "documents.workflow.update")
                || User.HasClaim("permission", "forms.update"));
        var isOwner = document.OwnerUserId == currentUserGuid;
        var actionState = PostApprovalJsonHelper.DeserializeState(document.PostApprovalJson);

        var ownerName = "—";
        if (document.OwnerUserId != Guid.Empty)
        {
            ownerName = await db.Users.AsNoTracking()
                .Where(u => u.Id == document.OwnerUserId)
                .Select(u => (u.FirstName + " " + u.LastName).Trim())
                .FirstOrDefaultAsync(ct) ?? "—";
            if (string.IsNullOrWhiteSpace(ownerName)) ownerName = "—";
        }

        return Ok(new
        {
            document.Id,
            DocumentId = document.Id,
            DocumentTitle = document.Title,
            document.ReferenceNumber,
            OwnerName = ownerName,
            ApprovalStatus = ToClientStatus(document.WorkflowStatus),
            document.WorkflowName,
            document.WorkflowStartedAtUtc,
            document.WorkflowScheduledStartAtUtc,
            document.CurrentStepOrder,
            CanAct = canAct,
            IsAwaitingMyApproval = canAct,
            IsAwaitingMyAction = isAwaitingMyAction,
            ListTag = ResolveListTag(document, canAct, isAwaitingMyAction),
            CanUpdateAction = canUpdateAction,
            ActionPhase = actionPhase,
            CanAssignWorkflow = DocumentWorkflowAccessRules.CanAssignWorkflow(document),
            CanStartWorkflow = DocumentWorkflowAccessRules.CanStartWorkflow(document),
            CanUnassignWorkflow = DocumentWorkflowAccessRules.CanUnassignWorkflow(document),
            SuggestedWorkflowTemplateId = document.WorkflowTemplateId,
            WorkflowRunsHistory = DocumentWorkflowRunHistoryHelper.Deserialize(document.WorkflowRunsHistoryJson),
            WorkflowRejection = DocumentWorkflowRejectionHelper.BuildView(document, isOwner),
            document.WorkflowRunCycle,
            IsWorkflowRerun = document.WorkflowRunCycle > 1,
            HasActionPhase = actionState is { AssigneeUserIds.Count: > 0 },
            ActionDirectionLabel = actionState?.ActionDirectionLabel,
            ActionPhaseStatus = actionState?.Status,
            Steps = steps.Select(s => new
            {
                s.Order,
                s.UserId,
                s.UserName,
                UserFirstName = s.UserFirstName,
                UserLastName = s.UserLastName,
                UserPositionTitle = s.UserPositionTitle,
                s.UserGender,
                s.Status,
                s.ActionAt,
                s.Note,
                s.Comment,
                s.ReviewCycle,
                SignatureUrl = s.SignatureUrl,
                SignatureWidthPx = SignatureWidthPx(s.SignatureDisplayDegree),
            }),
        });
    }

    [HttpPost("{id:guid}/action-status")]
    [Authorize(Policy = "documents.workflow.update")]
    public async Task<IActionResult> UpdateActionStatus(
        Guid id,
        [FromBody] WorkflowRunActionStatusRequest req,
        CancellationToken ct)
    {
        var document = await GetAuthorizedDocumentAsync(id, ct);
        if (document is null) return NotFound(new { message = "گردش سند یافت نشد" });

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var currentUserGuid)) return Unauthorized();

        var (ok, error) = await postApproval.UpdateStatusAsync(
            id, currentUserGuid, req.Status ?? "", req.Note, ct);
        if (!ok)
            return BadRequest(new { message = error ?? "به‌روزرسانی وضعیت اقدام ناموفق بود" });

        return Ok(new { message = "وضعیت اقدام ذخیره شد" });
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "documents.workflow.update")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] WorkflowRunActionRequest req, CancellationToken ct)
    {
        var document = await GetAuthorizedDocumentAsync(id, ct);
        if (document is null) return NotFound(new { message = "گردش سند یافت نشد" });

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
    [Authorize(Policy = "documents.workflow.update")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] WorkflowRunActionRequest req, CancellationToken ct)
    {
        var document = await GetAuthorizedDocumentAsync(id, ct);
        if (document is null) return NotFound(new { message = "گردش سند یافت نشد" });

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var currentUserGuid)) return Unauthorized();
        var result = await workflowProcessor.ProcessActionAsync(id, currentUserGuid, false, req.Comment, ct);
        if (!result.Success)
            return StatusCode(result.HttpStatus ?? 400, new { message = result.Message, code = "workflow_action_failed" });
        return Ok(new { message = result.Message });
    }

    [HttpPost("{id:guid}/resend-approval-sms")]
    [Authorize(Policy = "documents.workflow.update")]
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

    [HttpGet("{id:guid}/signature")]
    [Authorize]
    public async Task<IActionResult> GetStepSignature(Guid id, [FromQuery] int stepOrder, CancellationToken ct = default)
    {
        if (stepOrder < 1) return BadRequest(new { message = "stepOrder نامعتبر است" });
        var document = await GetAuthorizedDocumentAsync(id, ct);
        if (document is null) return NotFound(new { message = "گردش سند یافت نشد" });

        var steps = DeserializeSteps(document.StepsJson);
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

    private async Task EnrichStepsAsync(Guid documentId, List<ApprovalStepDto> steps, CancellationToken ct)
    {
        var approverIds = steps.Select(s => s.UserId).Where(id => id != Guid.Empty).Distinct().ToList();
        if (approverIds.Count > 0)
        {
            var approvers = await db.Users.AsNoTracking()
                .Where(u => approverIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Gender,
                    u.SignatureImagePath,
                    u.SignatureDisplayDegree,
                    PositionTitle = u.UserPosition != null ? u.UserPosition.Name : null,
                })
                .ToListAsync(ct);
            var userSigs = approvers.ToDictionary(
                u => u.Id,
                u => (u.SignatureImagePath, u.SignatureDisplayDegree));
            foreach (var step in steps)
            {
                var profile = approvers.FirstOrDefault(u => u.Id == step.UserId);
                if (profile is null) continue;
                FormApprovalSignatureHelper.EnrichApproverIdentityFromProfile(
                    step, profile.FirstName, profile.LastName, profile.PositionTitle, profile.Gender);
            }
            FormApprovalSignatureHelper.BackfillApprovedStepSignatures(steps, userSigs);
        }

        FormApprovalSignatureHelper.EnrichSignatureUrls(
            steps,
            s => $"/api/admin/document-workflow-runs/{documentId}/signature?stepOrder={s.Order}");
    }

    private static List<ApprovalStepDto> DeserializeSteps(string? json) =>
        DocumentWorkflowProcessor.DeserializeSteps(json);

    private static int SignatureWidthPx(int? degree) => degree switch
    {
        30 => 90,
        45 => 110,
        60 => 140,
        75 => 170,
        90 => 200,
        _ => 140,
    };

    private static string ToClientStatus(DocumentWorkflowStatus status) => status switch
    {
        DocumentWorkflowStatus.Pending => "pending",
        DocumentWorkflowStatus.InProgress => "in_progress",
        DocumentWorkflowStatus.Approved => "approved",
        DocumentWorkflowStatus.Rejected => "rejected",
        _ => "none"
    };

    private static string ResolveListTag(
        Document document,
        bool isAwaitingMyApproval,
        bool isAwaitingMyAction)
    {
        if (isAwaitingMyAction) return "awaiting_my_action";
        if (isAwaitingMyApproval) return "awaiting_me";

        if (document.WorkflowStatus == DocumentWorkflowStatus.Approved)
        {
            if (DocumentActionPhaseHelper.HasActiveActionPhase(document))
                return "action_phase";
            return "completed";
        }

        return document.WorkflowStatus switch
        {
            DocumentWorkflowStatus.Rejected => "rejected",
            DocumentWorkflowStatus.InProgress => "in_progress",
            DocumentWorkflowStatus.Pending => "pending_start",
            _ => "in_progress"
        };
    }

    private async Task<(List<Document> Page, int Total)> PaginateAwaitingMeAsync(
        IQueryable<Document> query,
        Guid currentUserGuid,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        const int scanBatch = 80;
        var ordered = query.OrderByDescending(x => x.WorkflowStartedAtUtc ?? x.UpdatedAtUtc);
        var matchedTotal = 0;
        var pageItems = new List<Document>();
        var startIndex = (page - 1) * pageSize;
        var offset = 0;

        while (true)
        {
            var batch = await ordered.Skip(offset).Take(scanBatch).ToListAsync(ct);
            if (batch.Count == 0) break;
            offset += batch.Count;

            foreach (var document in batch)
            {
                var steps = DeserializeSteps(document.StepsJson);
                if (!IsAwaitingUserApproval(document, steps, currentUserGuid)
                    && !DocumentActionPhaseHelper.IsAwaitingUserAction(document, currentUserGuid))
                    continue;

                if (matchedTotal >= startIndex && pageItems.Count < pageSize)
                    pageItems.Add(document);
                matchedTotal++;
            }

            if (batch.Count < scanBatch) break;
        }

        return (pageItems, matchedTotal);
    }

    private static bool IsAwaitingUserApproval(Document document, List<ApprovalStepDto> steps, Guid userGuid)
    {
        if (userGuid == Guid.Empty) return false;
        if (document.WorkflowStartedAtUtc is null) return false;
        if (document.WorkflowStatus != DocumentWorkflowStatus.InProgress) return false;
        var current = WorkflowStepJsonHelper.FindCurrentPending(steps, document.CurrentStepOrder);
        return current is not null && current.UserId == userGuid;
    }
}
