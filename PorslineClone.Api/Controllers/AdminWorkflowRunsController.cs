using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.StaticFiles;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/workflow-runs")]
[Authorize]
public class AdminWorkflowRunsController(
    AppDbContext db,
    IWebHostEnvironment env,
    FormWorkflowProcessor workflowProcessor,
    FormPostApprovalService postApproval,
    FormWorkflowRejectionService rejectionService) : ControllerBase
{
    private bool IsAdmin => User.IsInRole("Admin");
    private bool CanReadAllWorkflowRuns =>
        IsAdmin || User.HasClaim("permission", "workflow-runs.read.all");
    private bool CanReadWorkflowRuns =>
        IsAdmin
        || User.HasClaim("permission", "workflow-runs.read")
        || User.HasClaim("permission", "workflow-runs.read.all");
    private bool CanReadFormsArchive =>
        IsAdmin
        || User.HasClaim("permission", "forms.archive.read")
        || User.HasClaim("permission", "forms.archive.read.all");
    private bool CanReadAllFormsArchive =>
        IsAdmin || User.HasClaim("permission", "forms.archive.read.all");

    private static bool UserIsInSubmissionWorkflow(FormSubmission submission, Guid userId)
    {
        var steps = FormWorkflowProcessor.DeserializeSteps(submission.StepsJson);
        return steps.Any(s => s.UserId == userId);
    }

    private async Task<bool> UserSentDispatchLinkAsync(FormSubmission submission, Guid userId, CancellationToken ct)
    {
        if (submission.DispatchLinkId is not { } linkId) return false;
        return await db.FormDispatchLinks.AnyAsync(
            l => l.Id == linkId && l.SentByUserId == userId, ct);
    }

    private async Task<bool> UserCanAccessSubmissionAsync(
        FormSubmission submission,
        Guid userId,
        string? currentUserId,
        CancellationToken ct)
    {
        _ = currentUserId;
        if (FormVisibilityQuery.CanReadAllFormSubmissions(User))
            return true;
        if (UserIsInSubmissionWorkflow(submission, userId))
            return true;
        if (submission.Form is not null && FormVisibilityQuery.UserOwnsForm(submission.Form, userId))
            return true;
        if (await db.FormUserAccesses.AnyAsync(a => a.FormId == submission.FormId && a.UserId == userId, ct))
            return true;
        return await UserSentDispatchLinkAsync(submission, userId, ct);
    }

    private IQueryable<FormSubmission> ScopeVisibleSubmissions(
        IQueryable<FormSubmission> query,
        Guid userId,
        string? currentUserId)
    {
        _ = userId;
        _ = currentUserId;
        return query.ApplyVisibleFormSubmissions(db, User);
    }

    private async Task<FormSubmission?> GetAuthorizedSubmissionAsync(
        Guid id,
        CancellationToken ct,
        bool allowArchivedRead = false)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var userGuid))
            return null;

        var submission = await db.FormSubmissions
            .Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == id && x.Form != null && !x.Form.IsDeleted, ct);
        if (submission is null || !HasWorkflowRun(submission))
            return null;

        if (submission.IsArchived)
        {
            if (!allowArchivedRead || !CanReadFormsArchive)
                return null;
        }
        else if (!CanReadWorkflowRuns)
        {
            return null;
        }

        if (!await UserCanAccessSubmissionAsync(submission, userGuid, currentUserId, ct))
            return null;

        return submission;
    }

    private IQueryable<FormSubmission> ScopeVisibleArchiveSubmissions(
        IQueryable<FormSubmission> query,
        Guid userId,
        string? currentUserId)
    {
        if (CanReadAllFormsArchive)
            return query;

        _ = currentUserId;
        return query.ApplyVisibleFormSubmissions(db, User);
    }

    private static bool HasWorkflowRun(FormSubmission submission) =>
        submission.WorkflowTemplateId is not null
        || submission.WorkflowStartedAtUtc is not null
        || (!string.IsNullOrWhiteSpace(submission.StepsJson) && submission.StepsJson.Trim() != "[]");

    [HttpGet]
    [Authorize(Policy = "workflow-runs.read")]
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

        var q = db.FormSubmissions
            .Include(x => x.Form)
            .Where(x => x.Form != null && !x.Form.IsDeleted)
            .Where(x => !x.IsArchived)
            .Where(x =>
                x.WorkflowTemplateId != null
                || x.WorkflowStartedAtUtc != null
                || (x.StepsJson != null && x.StepsJson != "" && x.StepsJson != "[]"));

        q = ScopeVisibleSubmissions(q, currentUserGuid, currentUserId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                (x.SubmitterName ?? "").Contains(s)
                || (x.SubmitterEmail ?? "").Contains(s)
                || x.Form.Title.Contains(s)
                || (x.WorkflowName ?? "").Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim().ToLowerInvariant();
            q = st switch
            {
                "approved" => q.Where(x => x.Status == FormSubmissionStatus.Approved),
                "rejected" => q.Where(x => x.Status == FormSubmissionStatus.Rejected),
                "in_progress" => q.Where(x => x.Status == FormSubmissionStatus.InProgress),
                "pending" => q.Where(x => x.Status == FormSubmissionStatus.Pending),
                _ => q
            };
        }

        List<FormSubmission> data;
        int total;
        if (awaitingMe)
        {
            (data, total) = await PaginateAwaitingMeAsync(q, currentUserGuid, page, pageSize, ct);
        }
        else
        {
            total = await q.CountAsync(ct);
            data = await q
                .OrderByDescending(x => x.SubmittedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(ct);
        }

        var items = data.Select(x =>
        {
            var steps = DeserializeSteps(x.StepsJson);
            var approved = steps.Count(s => string.Equals(s.Status, "approved", StringComparison.OrdinalIgnoreCase));
            var isAwaitingMyApproval = IsAwaitingUserApproval(x, steps, currentUserGuid);
            var isAwaitingMyAction = FormActionPhaseHelper.IsAwaitingUserAction(x, currentUserGuid);
            var actionState = PostApprovalJsonHelper.DeserializeState(x.PostApprovalJson);
            var hasActionPhase = actionState is { AssigneeUserIds.Count: > 0 }
                || (x.Status == FormSubmissionStatus.Approved && x.WorkflowTemplateId is not null);
            var listTag = ResolveListTag(x, isAwaitingMyApproval, isAwaitingMyAction);
            return new
            {
                x.Id,
                x.FormId,
                FormTitle = x.Form.Title,
                x.SubmittedAtUtc,
                SubmitterName = x.SubmitterName,
                SubmitterMobile = x.SubmitterEmail,
                ApprovalStatus = ToClientStatus(x.Status),
                x.WorkflowName,
                StepCount = steps.Count,
                ApprovedStepCount = approved,
                x.WorkflowStartedAtUtc,
                CurrentStepOrder = x.CurrentStepOrder,
                IsAwaitingMyApproval = isAwaitingMyApproval,
                IsAwaitingMyAction = isAwaitingMyAction,
                HasActionPhase = hasActionPhase && FormActionPhaseHelper.HasActiveActionPhase(x),
                ActionDirectionLabel = actionState?.ActionDirectionLabel,
                ActionPhaseStatus = actionState?.Status,
                ActionPhaseStatusLabel = actionState is null
                    ? null
                    : PostApprovalJsonHelper.StatusLabel(actionState.Status),
                ListTag = listTag,
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

    [HttpGet("archive")]
    [Authorize(Policy = "forms.archive.read")]
    public async Task<IActionResult> ListArchive(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var currentUserGuid))
            return Unauthorized();

        var q = db.FormSubmissions
            .Include(x => x.Form)
            .Where(x => x.Form != null && !x.Form.IsDeleted)
            .Where(x => x.IsArchived)
            .Where(x =>
                x.WorkflowTemplateId != null
                || x.WorkflowStartedAtUtc != null
                || (x.StepsJson != null && x.StepsJson != "" && x.StepsJson != "[]"));

        q = ScopeVisibleArchiveSubmissions(q, currentUserGuid, currentUserId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                (x.SubmitterName ?? "").Contains(s)
                || (x.SubmitterEmail ?? "").Contains(s)
                || x.Form.Title.Contains(s)
                || (x.WorkflowName ?? "").Contains(s));
        }

        if (!string.IsNullOrWhiteSpace(status))
        {
            var st = status.Trim().ToLowerInvariant();
            q = st switch
            {
                "approved" => q.Where(x => x.Status == FormSubmissionStatus.Approved),
                "rejected" => q.Where(x => x.Status == FormSubmissionStatus.Rejected),
                "in_progress" => q.Where(x => x.Status == FormSubmissionStatus.InProgress),
                "pending" => q.Where(x => x.Status == FormSubmissionStatus.Pending),
                _ => q
            };
        }

        var total = await q.CountAsync(ct);
        var data = await q
            .OrderByDescending(x => x.SubmittedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = data.Select(x =>
        {
            var steps = DeserializeSteps(x.StepsJson);
            var approved = steps.Count(s => string.Equals(s.Status, "approved", StringComparison.OrdinalIgnoreCase));
            var actionState = PostApprovalJsonHelper.DeserializeState(x.PostApprovalJson);
            return new
            {
                x.Id,
                x.FormId,
                FormTitle = x.Form.Title,
                x.SubmittedAtUtc,
                SubmitterName = x.SubmitterName,
                SubmitterMobile = x.SubmitterEmail,
                ApprovalStatus = ToClientStatus(x.Status),
                x.WorkflowName,
                StepCount = steps.Count,
                ApprovedStepCount = approved,
                x.WorkflowStartedAtUtc,
                x.CurrentStepOrder,
                IsArchived = true,
                ActionPhaseStatusLabel = actionState is null
                    ? null
                    : PostApprovalJsonHelper.StatusLabel(actionState.Status),
                ListTag = "completed",
                x.WorkflowRunCycle,
                IsWorkflowRerun = x.WorkflowRunCycle > 1,
                CanRestartWorkflow = x.Status == FormSubmissionStatus.Rejected
                    && x.IsArchived
                    && x.WorkflowStartedAtUtc != null,
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
    [Authorize]
    public async Task<IActionResult> Detail(Guid id, CancellationToken ct = default)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct, allowArchivedRead: true);
        if (submission is null) return NotFound(new { message = "گردش کار یافت نشد" });

        if (!submission.IsArchived && submission.Status == FormSubmissionStatus.Approved)
            await postApproval.TryStartPostApprovalAsync(submission, ct);

        var values = string.IsNullOrWhiteSpace(submission.FieldsJson)
            ? new List<FormFieldValueDto>()
            : (JsonSerializer.Deserialize<List<FormFieldValueDto>>(submission.FieldsJson) ?? new List<FormFieldValueDto>());

        var uploadPaths = FormSubmissionUploadHelper.ListUploadPaths(values);
        var fileValues = uploadPaths
            .Select((url, i) =>
            {
                FormSubmissionUploadHelper.TryResolveDiskPath(env, url, out var filePath);
                var fileInfo = new FileInfo(filePath);
                return new
                {
                    Index = i,
                    Label = values.FirstOrDefault(v => FormSubmissionUploadHelper.NormalizeRelativePath(v.Value) == url)?.Label ?? "",
                    Url = url,
                    FileName = Path.GetFileName(url),
                    SizeBytes = fileInfo.Exists ? fileInfo.Length : 0L,
                    Kind = FormSubmissionUploadHelper.FileKindFromPath(url),
                    DownloadUrl = $"/api/admin/workflow-runs/{submission.Id}/files/{i}/download",
                    MissingOnDisk = !fileInfo.Exists,
                };
            })
            .ToList();

        var steps = DeserializeSteps(submission.StepsJson);
        await EnrichStepsAsync(submission.Id, steps, ct);

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(currentUserId, out var currentUserGuid);
        var canAct = IsAwaitingUserApproval(submission, steps, currentUserGuid);
        var isAwaitingMyAction = FormActionPhaseHelper.IsAwaitingUserAction(submission, currentUserGuid);
        var actionPhase = await FormActionPhaseHelper.BuildViewAsync(submission, db, ct);
        var actionState = PostApprovalJsonHelper.DeserializeState(submission.PostApprovalJson);
        var canUpdateAction = actionState is not null
            && actionState.AssigneeUserIds.Contains(currentUserGuid)
            && !string.Equals(actionState.Status, "completed", StringComparison.OrdinalIgnoreCase);

        return Ok(new
        {
            submission.Id,
            submission.FormId,
            FormTitle = submission.Form.Title,
            submission.SubmittedAtUtc,
            SubmitterName = submission.SubmitterName,
            SubmitterMobile = submission.SubmitterEmail,
            ApprovalStatus = ToClientStatus(submission.Status),
            submission.WorkflowName,
            submission.WorkflowStartedAtUtc,
            submission.CurrentStepOrder,
            CanAct = canAct,
            IsAwaitingMyApproval = canAct,
            IsAwaitingMyAction = isAwaitingMyAction,
            CanUpdateAction = canUpdateAction,
            ActionPhase = actionPhase,
            ListTag = ResolveListTag(submission, canAct, isAwaitingMyAction),
            submission.WorkflowRunCycle,
            IsWorkflowRerun = submission.WorkflowRunCycle > 1,
            CanRestartWorkflow = FormSubmissionWorkflowAccessRules.CanRestartWorkflowAfterReject(submission),
            WorkflowRejection = FormWorkflowRejectionHelper.BuildView(
                submission,
                await rejectionService.IsDispatchSenderAsync(
                    submission,
                    currentUserGuid,
                    IsAdmin,
                    ct)),
            CanAssignWorkflow = FormSubmissionWorkflowAccessRules.CanAssignWorkflow(submission),
            CanStartWorkflow = FormSubmissionWorkflowAccessRules.CanStartWorkflow(submission),
            SuggestedWorkflowTemplateId = submission.WorkflowTemplateId ?? submission.Form.WorkflowTemplateId,
            WorkflowRunsHistory = FormWorkflowRunHistoryHelper.Deserialize(submission.WorkflowRunsHistoryJson),
            submission.IsArchived,
            Fields = values.Select(v => new
            {
                v.Label,
                v.Value,
                IsFile = FormSubmissionUploadHelper.IsUploadPath(v.Value),
                File = fileValues.FirstOrDefault(f =>
                    f.Url == FormSubmissionUploadHelper.NormalizeRelativePath(v.Value))
            }),
            Files = fileValues,
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

    [HttpPost("{id:guid}/approve")]
    [Authorize(Policy = "workflow-runs.update")]
    public async Task<IActionResult> Approve(Guid id, [FromBody] WorkflowRunActionRequest req, CancellationToken ct)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "گردش کار یافت نشد" });

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

    [HttpPost("{id:guid}/action-status")]
    [Authorize(Policy = "workflow-runs.update")]
    public async Task<IActionResult> UpdateActionStatus(
        Guid id,
        [FromBody] WorkflowRunActionStatusRequest req,
        CancellationToken ct)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "گردش کار یافت نشد" });

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var currentUserGuid)) return Unauthorized();

        var (ok, error) = await postApproval.UpdateStatusAsync(
            id, currentUserGuid, req.Status ?? "", req.Note, ct);
        if (!ok)
            return BadRequest(new { message = error ?? "به‌روزرسانی وضعیت اقدام ناموفق بود" });

        return Ok(new { message = "وضعیت اقدام ذخیره شد" });
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "workflow-runs.update")]
    public async Task<IActionResult> Reject(Guid id, [FromBody] WorkflowRunActionRequest req, CancellationToken ct)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "گردش کار یافت نشد" });

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(currentUserId, out var currentUserGuid)) return Unauthorized();
        var result = await workflowProcessor.ProcessActionAsync(id, currentUserGuid, false, req.Comment, ct);
        if (!result.Success)
            return StatusCode(result.HttpStatus ?? 400, new { message = result.Message, code = "workflow_action_failed" });
        return Ok(new { message = result.Message });
    }

    [HttpGet("{id:guid}/signature")]
    [Authorize]
    public async Task<IActionResult> GetStepSignature(Guid id, [FromQuery] int stepOrder, CancellationToken ct = default)
    {
        if (stepOrder < 1) return BadRequest(new { message = "stepOrder نامعتبر است" });
        var submission = await GetAuthorizedSubmissionAsync(id, ct, allowArchivedRead: true);
        if (submission is null) return NotFound(new { message = "گردش کار یافت نشد" });

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
    [Authorize]
    public async Task<IActionResult> DownloadFile(Guid id, int index, CancellationToken ct = default)
    {
        if (index < 0) return BadRequest(new { message = "index نامعتبر است" });
        var submission = await GetAuthorizedSubmissionAsync(id, ct, allowArchivedRead: true);
        if (submission is null) return NotFound(new { message = "گردش کار یافت نشد" });

        var values = string.IsNullOrWhiteSpace(submission.FieldsJson)
            ? new List<FormFieldValueDto>()
            : (JsonSerializer.Deserialize<List<FormFieldValueDto>>(submission.FieldsJson) ?? new List<FormFieldValueDto>());
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

    private async Task EnrichStepsAsync(Guid submissionId, List<ApprovalStepDto> steps, CancellationToken ct)
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
            s => $"/api/admin/workflow-runs/{submissionId}/signature?stepOrder={s.Order}");
    }

    private static List<ApprovalStepDto> DeserializeSteps(string? json) =>
        FormWorkflowProcessor.DeserializeSteps(json);

    private static int SignatureWidthPx(int? degree) => degree switch
    {
        30 => 90,
        45 => 110,
        60 => 140,
        75 => 170,
        90 => 200,
        _ => 140,
    };

    private static string ToClientStatus(FormSubmissionStatus status) => status switch
    {
        FormSubmissionStatus.Pending => "pending",
        FormSubmissionStatus.InProgress => "in_progress",
        FormSubmissionStatus.Approved => "approved",
        FormSubmissionStatus.Rejected => "rejected",
        FormSubmissionStatus.Submitted => "submitted",
        _ => "pending"
    };

    /// <summary>صفحه‌بندی «نوبت من» بدون بارگذاری یکجای همه رکوردها در حافظه.</summary>
    private async Task<(List<FormSubmission> Page, int Total)> PaginateAwaitingMeAsync(
        IQueryable<FormSubmission> query,
        Guid currentUserGuid,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        const int scanBatch = 80;
        var ordered = query.OrderByDescending(x => x.SubmittedAtUtc);
        var matchedTotal = 0;
        var pageItems = new List<FormSubmission>();
        var startIndex = (page - 1) * pageSize;
        var offset = 0;

        while (true)
        {
            var batch = await ordered.Skip(offset).Take(scanBatch).ToListAsync(ct);
            if (batch.Count == 0) break;
            offset += batch.Count;

            foreach (var submission in batch)
            {
                var steps = DeserializeSteps(submission.StepsJson);
                if (!IsAwaitingUserApproval(submission, steps, currentUserGuid)
                    && !FormActionPhaseHelper.IsAwaitingUserAction(submission, currentUserGuid))
                    continue;

                if (matchedTotal >= startIndex && pageItems.Count < pageSize)
                    pageItems.Add(submission);
                matchedTotal++;
            }

            if (batch.Count < scanBatch) break;
        }

        return (pageItems, matchedTotal);
    }

    private static bool IsAwaitingUserApproval(FormSubmission submission, List<ApprovalStepDto> steps, Guid userGuid)
    {
        if (userGuid == Guid.Empty) return false;
        if (submission.WorkflowStartedAtUtc is null) return false;
        if (submission.Status != FormSubmissionStatus.InProgress) return false;
        var current = WorkflowStepJsonHelper.FindCurrentPending(steps, submission.CurrentStepOrder);
        return current is not null && current.UserId == userGuid;
    }

    private static string ResolveListTag(
        FormSubmission submission,
        bool isAwaitingMyApproval,
        bool isAwaitingMyAction)
    {
        if (isAwaitingMyAction) return "awaiting_my_action";
        if (isAwaitingMyApproval) return "awaiting_me";

        if (submission.Status == FormSubmissionStatus.Approved)
        {
            if (FormActionPhaseHelper.HasActiveActionPhase(submission))
                return "action_phase";
            return "completed";
        }

        return submission.Status switch
        {
            FormSubmissionStatus.Rejected => "rejected",
            FormSubmissionStatus.InProgress => "in_progress",
            FormSubmissionStatus.Pending => "pending_start",
            _ => "in_progress"
        };
    }
}

public record WorkflowRunActionRequest(string? Comment);

public record WorkflowRunActionStatusRequest(string? Status, string? Note);
