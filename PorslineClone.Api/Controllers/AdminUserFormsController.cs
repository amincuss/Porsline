using System.Security.Claims;
using System.IO;
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
[Route("api/admin/user-forms")]
[Authorize]
public class AdminUserFormsController(
    AppDbContext db,
    IWebHostEnvironment env,
    FormWorkflowProcessor workflowProcessor,
    FormWorkflowRejectionService rejectionService) : ControllerBase
{
    private async Task<FormSubmission?> GetAuthorizedSubmissionAsync(Guid id, CancellationToken ct)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("Admin");
        var submission = await db.FormSubmissions
            .Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == id && x.Form != null && !x.Form.IsDeleted, ct);
        if (submission is null) return null;
        if (!isAdmin)
        {
            if (!Guid.TryParse(currentUserId, out var userGuid))
                return null;
            var ownsForm = submission.Form.UserId == currentUserId;
            var sentLink = submission.DispatchLinkId is { } linkId
                && await db.FormDispatchLinks.AnyAsync(
                    l => l.Id == linkId && l.SentByUserId == userGuid, ct);
            if (!ownsForm && !sentLink)
                return null;
        }
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
        pageSize = Math.Clamp(pageSize, 5, 50);
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
                (x.TrackingCode ?? "").Contains(s) ||
                x.Form.Title.Contains(s));
        }

        if (!isAdmin && currentUserGuid != Guid.Empty)
        {
            q = q.Where(x =>
                x.Form.UserId == currentUserId
                || (x.DispatchLinkId != null
                    && db.FormDispatchLinks.Any(l =>
                        l.Id == x.DispatchLinkId && l.SentByUserId == currentUserGuid)));
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

        if (page == 1)
            await ProcessDueScheduledWorkflowStartsAsync(ct);

        var total = await q.CountAsync(ct);
        var data = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var dispatchLinkIds = data
            .Where(x => x.DispatchLinkId is not null)
            .Select(x => x.DispatchLinkId!.Value)
            .Distinct()
            .ToList();
        var dispatchTemplateByLinkId = dispatchLinkIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await db.FormDispatchLinks.AsNoTracking()
                .Where(l => dispatchLinkIds.Contains(l.Id))
                .ToDictionaryAsync(l => l.Id, l => (Guid?)l.WorkflowTemplateId, ct);
        HashSet<Guid> senderLinkIds;
        if (isAdmin || currentUserGuid == Guid.Empty || dispatchLinkIds.Count == 0)
            senderLinkIds = new HashSet<Guid>();
        else
        {
            var ownedLinks = await db.FormDispatchLinks.AsNoTracking()
                .Where(l => dispatchLinkIds.Contains(l.Id) && l.SentByUserId == currentUserGuid)
                .Select(l => l.Id)
                .ToListAsync(ct);
            senderLinkIds = ownedLinks.ToHashSet();
        }

        var result = new List<object>();
        foreach (var x in data)
        {
            var isSender = isAdmin
                || (x.DispatchLinkId is Guid linkId && senderLinkIds.Contains(linkId));
            var steps = FormWorkflowProcessor.DeserializeSteps(x.StepsJson);
            var latest = steps
                .Where(s => s.Status == "approved" || s.Status == "rejected")
                .OrderByDescending(s => s.ActionAt ?? DateTime.MinValue)
                .FirstOrDefault();
            Guid? dispatchWorkflowTemplateId = null;
            if (x.DispatchLinkId is Guid dlId && dispatchTemplateByLinkId.TryGetValue(dlId, out var tplId))
                dispatchWorkflowTemplateId = tplId;

            result.Add(new
            {
                x.Id,
                x.FormId,
                FormTitle = x.Form.Title,
                x.SubmittedAtUtc,
                SubmitterName = x.SubmitterName,
                SubmitterMobile = x.SubmitterEmail,
                TrackingCode = x.TrackingCode,
                ApprovalStatus = ToClientStatus(x.Status),
                SuggestedWorkflowTemplateId = x.WorkflowTemplateId ?? dispatchWorkflowTemplateId ?? x.Form.WorkflowTemplateId,
                SuggestedWorkflowName = x.WorkflowName ?? x.Form.WorkflowName,
                LatestApprover = latest?.UserName,
                LatestApproverActionAt = latest?.ActionAt,
                IsApprovalCompleted = x.Status is FormSubmissionStatus.Approved or FormSubmissionStatus.Rejected,
                x.WorkflowName,
                x.WorkflowTemplateId,
                x.WorkflowStartedAtUtc,
                x.WorkflowScheduledStartAtUtc,
                x.WorkflowRunCycle,
                IsWorkflowRerun = x.WorkflowRunCycle > 1,
                WorkflowRejection = FormWorkflowRejectionHelper.BuildView(x, isSender),
                CanRestartWorkflow = CanRestartWorkflowAfterReject(x),
                CanStartWorkflow = CanStartWorkflow(x),
                CanAssignWorkflow = CanAssignWorkflow(x),
                CanUnassignWorkflow = CanUnassignWorkflow(x),
                HasWorkflowAssigned = HasAssignedWorkflow(x),
                NeedsWorkflowStart = x.Status == FormSubmissionStatus.Pending && x.WorkflowTemplateId is not null,
                x.IsArchived,
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

        var fieldTypesByLabel = await db.FormFields.AsNoTracking()
            .Where(ff => ff.FormId == submission.FormId)
            .GroupBy(ff => ff.Label)
            .Select(g => new { Label = g.Key, FieldType = (int)g.First().FieldType })
            .ToDictionaryAsync(x => x.Label, x => x.FieldType, ct);

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
                    DownloadUrl = $"/api/admin/user-forms/{submission.Id}/files/{i}/download",
                    MissingOnDisk = !fileInfo.Exists,
                };
            })
            .ToList();

        var steps = FormWorkflowProcessor.DeserializeSteps(submission.StepsJson);

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
            s => $"/api/admin/user-forms/{submission.Id}/signature?stepOrder={s.Order}");

        return Ok(new
        {
            submission.Id,
            submission.FormId,
            FormTitle = submission.Form.Title,
            submission.SubmittedAtUtc,
            SubmitterName = submission.SubmitterName,
            SubmitterMobile = submission.SubmitterEmail,
            TrackingCode = submission.TrackingCode,
            ApprovalStatus = ToClientStatus(submission.Status),
            SuggestedWorkflowTemplateId = submission.Form.WorkflowTemplateId,
            SuggestedWorkflowName = submission.Form.WorkflowName,
            Fields = values.Select(v => new
            {
                v.Label,
                v.Value,
                FieldType = fieldTypesByLabel.GetValueOrDefault(v.Label, 0),
                IsFile = FormSubmissionUploadHelper.IsUploadPath(v.Value),
                File = fileValues.FirstOrDefault(f =>
                    f.Url == FormSubmissionUploadHelper.NormalizeRelativePath(v.Value))
            }),
            Files = fileValues,
            Steps = steps.Select(s => new
            {
                s.Order,
                s.UserName,
                UserFirstName = s.UserFirstName,
                UserLastName = s.UserLastName,
                UserPositionTitle = s.UserPositionTitle,
                s.UserGender,
                s.Status,
                s.ActionAt,
                s.Note,
                s.Comment,
                SignatureUrl = s.SignatureUrl,
                SignatureWidthPx = SignatureWidthPx(s.SignatureDisplayDegree),
            }),
            submission.WorkflowName,
            submission.WorkflowTemplateId,
            submission.WorkflowStartedAtUtc,
            submission.WorkflowScheduledStartAtUtc,
            submission.WorkflowRunCycle,
            IsWorkflowRerun = submission.WorkflowRunCycle > 1,
            CanRestartWorkflow = CanRestartWorkflowAfterReject(submission),
            CanStartWorkflow = CanStartWorkflow(submission),
            CanAssignWorkflow = CanAssignWorkflow(submission),
            CanUnassignWorkflow = CanUnassignWorkflow(submission),
            HasWorkflowAssigned = HasAssignedWorkflow(submission),
            WorkflowRunsHistory = FormWorkflowRunHistoryHelper.Deserialize(submission.WorkflowRunsHistoryJson),
            submission.IsArchived,
            WorkflowRejection = FormWorkflowRejectionHelper.BuildView(
                submission,
                await rejectionService.IsDispatchSenderAsync(
                    submission,
                    Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var ug) ? ug : Guid.Empty,
                    User.IsInRole("Admin"),
                    ct)),
        });
    }

    [HttpPost("{id:guid}/request-reapproval")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> RequestReapproval(Guid id, CancellationToken ct)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var (ok, err) = await rejectionService.RequestReapprovalAsync(submission, userId, User.IsInRole("Admin"), ct);
        if (!ok) return BadRequest(new { message = err ?? "درخواست مجدد تأیید ناموفق بود" });

        return Ok(new { message = "درخواست مجدد تأیید ثبت شد. پیامک فوری برای تأییدکننده ارسال شد." });
    }

    [HttpPost("{id:guid}/end-workflow")]
    [Authorize(Policy = "forms.update")]
    public async Task<IActionResult> EndWorkflow(Guid id, CancellationToken ct)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            return Unauthorized();

        var (ok, err) = await rejectionService.EndWorkflowAsync(submission, userId, User.IsInRole("Admin"), ct);
        if (!ok) return BadRequest(new { message = err ?? "اتمام گردش ناموفق بود" });

        return Ok(new { message = "گردش خاتمه یافت و پرونده به بایگانی منتقل شد." });
    }

    [HttpPost("{id:guid}/assign-workflow")]
    [Authorize(Policy = "responders.userforms.workflow")]
    public async Task<IActionResult> AssignWorkflow(Guid id, [FromBody] AssignWorkflowRequest req, CancellationToken ct)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });
        if (!CanAssignWorkflow(submission))
            return BadRequest(new { message = BuildAssignWorkflowDeniedMessage(submission) });

        var isRestart = CanRestartWorkflowAfterReject(submission);
        if (isRestart && !User.HasClaim("permission", "responders.userforms.workflow.restart")
            && !User.HasClaim("permission", "responders.userforms.workflow")
            && !User.HasClaim("permission", "forms.update"))
            return StatusCode(403, new { message = "مجوز «گردش مجدد» ندارید" });

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

        if (isRestart)
        {
            FormWorkflowRunHistoryHelper.SnapshotCurrentRun(submission);
            submission.WorkflowRunCycle = Math.Max(1, submission.WorkflowRunCycle) + 1;
            submission.IsArchived = false;
            submission.PostApprovalJson = null;

            var links = await db.FormSubmissionApprovalLinks
                .Where(x => x.FormSubmissionId == id && x.IsActive)
                .ToListAsync(ct);
            foreach (var link in links)
                link.IsActive = false;
        }

        submission.WorkflowTemplateId = template.Id;
        submission.WorkflowName = template.Name;
        submission.WorkflowStartedAtUtc = null;
        submission.WorkflowScheduledStartAtUtc = mode == "scheduled" ? scheduledUtc : null;
        submission.Status = FormSubmissionStatus.Pending;
        submission.CurrentStepOrder = 1;
        var reviewCycle = isRestart ? submission.WorkflowRunCycle : 0;
        submission.StepsJson = WorkflowStepJsonHelper.Serialize(
            WorkflowStepBuilder.BuildApprovalStepsFromTemplate(template.StepsJson, startImmediately: false, reviewCycle));

        await db.SaveChangesAsync(ct);

        var cycleLabel = submission.WorkflowRunCycle > 1
            ? $" (دور {submission.WorkflowRunCycle})"
            : "";

        if (mode == "now")
        {
            await db.Entry(submission).ReloadAsync(ct);
            var (ok, err) = await workflowProcessor.TryStartWorkflowAsync(submission, ct);
            if (!ok) return BadRequest(new { message = err ?? "شروع گردش ناموفق بود" });
            return Ok(new
            {
                message = isRestart
                    ? $"گردش مجدد «{submission.WorkflowName}»{cycleLabel} انتصاب و شروع شد"
                    : $"گردش «{submission.WorkflowName}» انتصاب و شروع شد",
                workflowStartedAtUtc = submission.WorkflowStartedAtUtc,
                workflowRunCycle = submission.WorkflowRunCycle,
            });
        }

        if (mode == "scheduled")
        {
            return Ok(new
            {
                message = isRestart
                    ? $"گردش مجدد «{submission.WorkflowName}»{cycleLabel} انتصاب شد و در تاریخ برنامه‌ریزی‌شده شروع می‌شود"
                    : $"گردش «{submission.WorkflowName}» انتصاب شد و در تاریخ برنامه‌ریزی‌شده شروع می‌شود",
                workflowScheduledStartAtUtc = submission.WorkflowScheduledStartAtUtc,
                workflowRunCycle = submission.WorkflowRunCycle,
            });
        }

        return Ok(new
        {
            message = isRestart
                ? $"گردش مجدد «{submission.WorkflowName}»{cycleLabel} انتصاب شد. برای شروع دکمه «شروع گردش» را بزنید"
                : $"گردش «{submission.WorkflowName}» انتصاب شد. برای شروع دکمه «شروع گردش» را بزنید",
            canStartWorkflow = true,
            workflowRunCycle = submission.WorkflowRunCycle,
        });
    }

    [HttpPost("{id:guid}/unassign-workflow")]
    [Authorize(Policy = "responders.userforms.workflow")]
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
    [Authorize(Policy = "responders.userforms.workflow")]
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
        FormSubmissionWorkflowAccessRules.HasAssignedWorkflow(submission);

    private static bool HasWorkflowActivity(FormSubmission submission) =>
        FormSubmissionWorkflowAccessRules.HasWorkflowActivity(submission);

    private static bool CanRestartWorkflowAfterReject(FormSubmission submission) =>
        FormSubmissionWorkflowAccessRules.CanRestartWorkflowAfterReject(submission);

    private static bool CanAssignWorkflow(FormSubmission submission) =>
        FormSubmissionWorkflowAccessRules.CanAssignWorkflow(submission);

    private static string BuildAssignWorkflowDeniedMessage(FormSubmission submission)
    {
        if (submission.Status == FormSubmissionStatus.InProgress)
            return "این پرونده در حال گردش است؛ تا پایان گردش فعلی امکان اتصال گردش جدید وجود ندارد";
        if (HasAssignedWorkflow(submission) && submission.WorkflowStartedAtUtc is null)
            return "گردش قبلاً انتصاب شده است؛ ابتدا آن را شروع کنید یا لغو کنید";
        if (HasWorkflowActivity(submission) && submission.Status != FormSubmissionStatus.Rejected)
            return "در وضعیت فعلی امکان انتصاب گردش وجود ندارد";
        return "در وضعیت فعلی امکان انتصاب گردش وجود ندارد";
    }

    private static bool CanUnassignWorkflow(FormSubmission submission) =>
        submission.WorkflowStartedAtUtc is null
        && submission.Status is not FormSubmissionStatus.InProgress
        && HasAssignedWorkflow(submission);

    private static bool CanStartWorkflow(FormSubmission submission) =>
        FormSubmissionWorkflowAccessRules.CanStartWorkflow(submission);

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

    [HttpGet("{id:guid}/signature")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> GetStepSignature(Guid id, [FromQuery] int stepOrder, CancellationToken ct = default)
    {
        if (stepOrder < 1) return BadRequest(new { message = "stepOrder نامعتبر است" });

        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });

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
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> DownloadFile(Guid id, int index, CancellationToken ct = default)
    {
        if (index < 0) return BadRequest(new { message = "index نامعتبر است" });
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });

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

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "responders.userforms.delete")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct = default)
    {
        var submission = await GetAuthorizedSubmissionAsync(id, ct);
        if (submission is null) return NotFound(new { message = "پاسخ فرم یافت نشد" });

        if (submission.Status == FormSubmissionStatus.InProgress)
            return BadRequest(new { message = "پاسخ در جریان گردش تأیید است؛ ابتدا گردش را لغو یا به پایان برسانید" });

        var values = string.IsNullOrWhiteSpace(submission.FieldsJson)
            ? new List<FormFieldValueDto>()
            : (JsonSerializer.Deserialize<List<FormFieldValueDto>>(submission.FieldsJson) ?? new List<FormFieldValueDto>());
        foreach (var path in FormSubmissionUploadHelper.ListUploadPaths(values))
        {
            if (!FormSubmissionUploadHelper.TryResolveDiskPath(env, path, out var fullPath)) continue;
            try { System.IO.File.Delete(fullPath); } catch { /* ignore */ }
        }

        var approvalLinks = await db.FormSubmissionApprovalLinks
            .Where(x => x.FormSubmissionId == id)
            .ToListAsync(ct);
        if (approvalLinks.Count > 0)
            db.FormSubmissionApprovalLinks.RemoveRange(approvalLinks);

        db.FormSubmissions.Remove(submission);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "پاسخ فرم حذف شد" });
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

