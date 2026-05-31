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
using PorslineClone.Api.Http;
using PorslineClone.Api.HangfireJobs;
using PorslineClone.Application.FormWordTemplates;
using PorslineClone.Infrastructure.Services.FormWordTemplates;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/user-forms")]
[Authorize]
public class AdminUserFormsController(
    AppDbContext db,
    IWebHostEnvironment env,
    FormWorkflowProcessor workflowProcessor,
    FormSubmissionWorkflowAssignService workflowAssignService,
    FormWorkflowRejectionService rejectionService,
    FormWordTemplateService wordTemplateService,
    FormWordBatchExportService wordBatchExportService,
    IFormWordBatchExportEnqueue wordBatchExportEnqueue) : ControllerBase
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

    [HttpGet("groups-sidebar")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> GroupsSidebar(CancellationToken ct = default)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(currentUserId, out var currentUserGuid);
        var isAdmin = User.IsInRole("Admin");

        var q = AuthorizedSubmissionsQuery(currentUserId, currentUserGuid, isAdmin);

        var countRows = await (
            from s in q
            where s.ResponderId != null
            join m in db.ResponderGroupMembers.AsNoTracking() on s.ResponderId equals m.ResponderId
            join g in db.ResponderGroups.AsNoTracking() on m.GroupId equals g.Id
            where !g.IsDeleted && g.IsActive
            group s by m.GroupId into grp
            select new { GroupId = grp.Key, Count = grp.Count() }
        ).ToListAsync(ct);

        var countByGroup = countRows.ToDictionary(x => x.GroupId, x => x.Count);

        var groups = await db.ResponderGroups.AsNoTracking()
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);

        var items = groups
            .Select(g => new
            {
                g.Id,
                g.Name,
                submissionCount = countByGroup.GetValueOrDefault(g.Id),
            })
            .Where(x => x.submissionCount > 0)
            .ToList();

        var inAnyGroup = db.ResponderGroupMembers.AsNoTracking().Select(m => m.ResponderId);
        var ungroupedCount = await q.CountAsync(
            x => x.ResponderId == null || !inAnyGroup.Contains(x.ResponderId.Value),
            ct);

        return Ok(new { groups = items, ungroupedCount });
    }

    [HttpGet]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = "submitted_desc",
        [FromQuery] string? status = null,
        [FromQuery] Guid? groupId = null,
        [FromQuery] bool ungroupedOnly = false,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(currentUserId, out var currentUserGuid);
        var isAdmin = User.IsInRole("Admin");

        var q = AuthorizedSubmissionsQuery(currentUserId, currentUserGuid, isAdmin);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                (x.SubmitterName ?? "").Contains(s) ||
                (x.SubmitterEmail ?? "").Contains(s) ||
                (x.TrackingCode ?? "").Contains(s) ||
                x.Form!.Title.Contains(s));
        }

        q = ApplyResponderGroupFilter(db, q, groupId, ungroupedOnly);

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

        if (!Guid.TryParse(req.WorkflowTemplateId, out var templateId))
            return BadRequest(new { message = "گردش انتخاب‌شده نامعتبر است" });

        var template = await db.FormWorkflowTemplates
            .FirstOrDefaultAsync(x => x.Id == templateId && x.IsActive, ct);
        if (template is null)
            return BadRequest(new { message = "گردش یافت نشد یا غیرفعال است" });

        var (ok, err, message) = await workflowAssignService.AssignAsync(submission, template, req, User, ct);
        if (!ok) return BadRequest(new { message = err ?? "انتصاب گردش ناموفق بود" });

        return Ok(new
        {
            message,
            workflowStartedAtUtc = submission.WorkflowStartedAtUtc,
            workflowScheduledStartAtUtc = submission.WorkflowScheduledStartAtUtc,
            workflowRunCycle = submission.WorkflowRunCycle,
            canStartWorkflow = submission.WorkflowStartedAtUtc is null && submission.Status == FormSubmissionStatus.Pending,
        });
    }

    [HttpPost("bulk-assign-workflow")]
    [Authorize(Policy = "responders.userforms.workflow")]
    public async Task<IActionResult> BulkAssignWorkflow([FromBody] BulkAssignFormWorkflowRequest req, CancellationToken ct)
    {
        if (!Guid.TryParse(req.WorkflowTemplateId, out var templateId))
            return BadRequest(new { message = "گردش انتخاب‌شده نامعتبر است" });

        var template = await db.FormWorkflowTemplates
            .FirstOrDefaultAsync(x => x.Id == templateId && x.IsActive, ct);
        if (template is null)
            return BadRequest(new { message = "گردش یافت نشد یا غیرفعال است" });

        List<Guid> submissionIds;
        if (req.AssignWholeGroup)
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(currentUserId, out var currentUserGuid);
            var isAdmin = User.IsInRole("Admin");
            var q = AuthorizedSubmissionsQuery(currentUserId, currentUserGuid, isAdmin);
            q = ApplyResponderGroupFilter(db, q, req.GroupId, req.UngroupedOnly);
            submissionIds = await q.Select(x => x.Id).ToListAsync(ct);
        }
        else
        {
            submissionIds = (req.SubmissionIds ?? [])
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();
        }

        if (submissionIds.Count == 0)
            return BadRequest(new { message = "هیچ پاسخی برای انتصاب گردش انتخاب نشده است" });

        var assignReq = new AssignWorkflowRequest(req.WorkflowTemplateId, req.StartMode, req.ScheduledStartAtUtc);
        var assignedCount = 0;
        var skippedCount = 0;
        var errors = new List<object>();

        foreach (var sid in submissionIds)
        {
            var submission = await GetAuthorizedSubmissionAsync(sid, ct);
            if (submission is null)
            {
                skippedCount++;
                errors.Add(new { submissionId = sid, message = "پاسخ یافت نشد یا دسترسی ندارید" });
                continue;
            }

            if (!CanAssignWorkflow(submission))
            {
                skippedCount++;
                errors.Add(new { submissionId = sid, message = BuildAssignWorkflowDeniedMessage(submission) });
                continue;
            }

            var (ok, err, _) = await workflowAssignService.AssignAsync(submission, template, assignReq, User, ct);
            if (!ok)
            {
                skippedCount++;
                errors.Add(new { submissionId = sid, message = err ?? "انتصاب ناموفق بود" });
                continue;
            }

            assignedCount++;
        }

        var summary = assignedCount > 0
            ? $"{assignedCount} پاسخ به گردش «{template.Name}» متصل شد"
            : "هیچ پاسخی متصل نشد";
        if (skippedCount > 0)
            summary += $" ({skippedCount} مورد رد شد)";

        return Ok(new
        {
            message = summary,
            assignedCount,
            skippedCount,
            errors = errors.Take(20),
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

    [HttpGet("grouped")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> Grouped(
        [FromQuery] Guid? formId,
        [FromQuery] Guid? groupId = null,
        [FromQuery] bool ungroupedOnly = false,
        CancellationToken ct = default)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(currentUserId, out var currentUserGuid);
        var isAdmin = User.IsInRole("Admin");
        var data = await wordTemplateService.GetGroupedSubmissionsAsync(
            formId, groupId, ungroupedOnly, currentUserId, isAdmin, currentUserGuid, ct);
        return Ok(data);
    }

    private IQueryable<FormSubmission> AuthorizedSubmissionsQuery(
        string? currentUserId,
        Guid currentUserGuid,
        bool isAdmin)
    {
        var q = db.FormSubmissions
            .Include(x => x.Form)
            .Where(x => x.Form != null && !x.Form.IsDeleted);

        if (!isAdmin && currentUserGuid != Guid.Empty)
        {
            q = q.Where(x =>
                x.Form!.UserId == currentUserId
                || (x.DispatchLinkId != null
                    && db.FormDispatchLinks.Any(l =>
                        l.Id == x.DispatchLinkId && l.SentByUserId == currentUserGuid)));
        }

        return q;
    }

    private static IQueryable<FormSubmission> ApplyResponderGroupFilter(
        AppDbContext db,
        IQueryable<FormSubmission> q,
        Guid? groupId,
        bool ungroupedOnly)
    {
        if (ungroupedOnly)
        {
            var inAnyGroup = db.ResponderGroupMembers.Select(m => m.ResponderId);
            return q.Where(x =>
                x.ResponderId == null || !inAnyGroup.Contains(x.ResponderId.Value));
        }

        if (groupId is { } gid && gid != Guid.Empty)
        {
            var memberIds = db.ResponderGroupMembers
                .Where(m => m.GroupId == gid)
                .Select(m => m.ResponderId);
            return q.Where(x => x.ResponderId != null && memberIds.Contains(x.ResponderId.Value));
        }

        return q;
    }

    [HttpPost("word-export-jobs")]
    [Authorize(Policy = "responders.update")]
    public async Task<IActionResult> StartWordExportJob(
        [FromBody] StartFormWordBatchExportRequest req,
        CancellationToken ct)
    {
        try
        {
            if (req.SubmissionIds is null || req.SubmissionIds.Count == 0)
                return BadRequest(new { message = "هیچ پاسخی انتخاب نشده است" });

            Guid? userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : null;

            var job = await wordBatchExportService.CreateQueuedJobAsync(
                req.TemplateId,
                req.SubmissionIds,
                req.ImageOverrides,
                userId,
                ct);

            var hangfireId = wordBatchExportEnqueue.Enqueue(job.Id);
            await wordBatchExportService.SetHangfireJobIdAsync(job.Id, hangfireId, ct);

            return Ok(new StartFormWordBatchExportResponse(
                job.Id,
                "تبدیل در پس‌زمینه شروع شد — پس از اتمام پیام دانلود ZIP نمایش داده می‌شود"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("word-export-jobs/{jobId:guid}")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> GetWordExportJobStatus(Guid jobId, CancellationToken ct)
    {
        var status = await wordBatchExportService.GetStatusAsync(jobId, ct);
        return status is null ? NotFound(new { message = "کار یافت نشد" }) : Ok(status);
    }

    [HttpGet("word-export-jobs/{jobId:guid}/download")]
    [Authorize(Policy = "responders.read")]
    public IActionResult DownloadWordExportJobZip(Guid jobId)
    {
        var full = wordBatchExportService.ResolveZipFullPath(jobId);
        if (full is null || !System.IO.File.Exists(full))
            return NotFound(new { message = "فایل ZIP یافت نشد" });

        var fileName = Path.GetFileName(full);
        ContentDispositionHelper.SetAttachment(Response, fileName);
        return PhysicalFile(full, "application/zip", fileName);
    }

    [HttpPost("generate-word-documents")]
    [Authorize(Policy = "responders.update")]
    public async Task<IActionResult> GenerateWordDocuments(
        [FromBody] GenerateWordDocumentsRequest req,
        CancellationToken ct)
    {
        try
        {
            var docs = await wordTemplateService.GenerateForSubmissionsAsync(
                req.TemplateId, req.SubmissionIds, req.ImageOverrides, ct);
            return Ok(new
            {
                message = $"{docs.Count} فایل Word تولید شد",
                items = docs.Select(d => new
                {
                    d.Id,
                    d.SubmissionId,
                    d.FileName,
                    downloadUrl = $"/api/admin/user-forms/word-documents/{d.Id}/download",
                }),
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("generate-word-documents-zip")]
    [Authorize(Policy = "responders.update")]
    public async Task<IActionResult> GenerateWordDocumentsZip(
        [FromBody] GenerateWordDocumentsRequest req,
        CancellationToken ct)
    {
        try
        {
            var (bytes, zipName) = await wordTemplateService.GenerateZipForSubmissionsAsync(
                req.TemplateId, req.SubmissionIds, req.ImageOverrides, ct);
            return ZipFileResult(bytes, zipName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("pack-word-documents-zip")]
    [Authorize(Policy = "responders.update")]
    public async Task<IActionResult> PackWordDocumentsZip(
        [FromBody] PackWordDocumentsZipRequest req,
        CancellationToken ct)
    {
        try
        {
            if (req.SubmissionIds is null || req.SubmissionIds.Count == 0)
                return BadRequest(new { message = "هیچ پاسخی انتخاب نشده است" });

            var (bytes, zipName) = await wordTemplateService.PackZipFromLatestDocumentsAsync(
                req.TemplateId, req.SubmissionIds, ct);
            return ZipFileResult(bytes, zipName);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    private IActionResult ZipFileResult(byte[] bytes, string zipName)
    {
        ContentDispositionHelper.SetAttachment(Response, zipName);
        return File(bytes, "application/zip", zipName);
    }

    [HttpGet("word-documents/{documentId:guid}/download")]
    [Authorize(Policy = "responders.read")]
    public IActionResult DownloadWordDocument(Guid documentId)
    {
        var full = wordTemplateService.ResolveExportFullPath(documentId);
        if (full is null || !System.IO.File.Exists(full))
            return NotFound(new { message = "فایل یافت نشد" });

        var fileName = Path.GetFileName(full);
        return PhysicalFile(full, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", fileName);
    }
}

public record GenerateWordDocumentsRequest(
    Guid TemplateId,
    List<Guid>? SubmissionIds,
    List<WordImageOverrideDto>? ImageOverrides = null);

public record PackWordDocumentsZipRequest(Guid TemplateId, List<Guid> SubmissionIds);

public record UpdateUserFormFieldRequest(string Label, string? Value);
public record UpdateUserFormRequest(string? SubmitterName, string? SubmitterMobile, List<UpdateUserFormFieldRequest>? Fields);

