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
using PorslineClone.Application.FormSubmissions;
using PorslineClone.Infrastructure.Services.FormWordTemplates;
using PorslineClone.Infrastructure.Services.FormSubmissions;

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
    IFormWordBatchExportEnqueue wordBatchExportEnqueue,
    FormSubmissionExcelExportService excelExportService,
    IFormSubmissionExcelExportEnqueue excelExportEnqueue,
    ResponderGroupSmsInquiryService smsInquiry,
    UserFormsGroupSidebarService groupSidebar) : ControllerBase
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

        var sidebarItems = await groupSidebar.BuildAsync(ct);
        var groups = sidebarItems.Select(x => new
        {
            id = x.Id,
            name = x.Name,
            submissionCount = x.SubmissionCount,
            pendingFormCount = x.PendingCount,
            dispatchedCount = x.DispatchedCount,
            memberCount = x.MemberCount,
            registeredMemberCount = x.RegisteredMemberCount,
            notRegisteredMemberCount = x.NotRegisteredMemberCount,
            primaryFormId = x.PrimaryFormId,
            primaryFormTitle = x.PrimaryFormTitle,
            duplicateResponderCount = x.DuplicateResponderCount,
            duplicateSubmissionCount = x.DuplicateSubmissionCount,
        }).ToList();

        var ungroupedCount = 0;
        try
        {
            var q = AuthorizedSubmissionsQuery(currentUserId, currentUserGuid, isAdmin);
            ungroupedCount = await q.CountAsync(
                x => x.ResponderId == null
                    || !db.ResponderGroupMembers.Any(m => m.ResponderId == x.ResponderId),
                ct);
        }
        catch
        {
            ungroupedCount = 0;
        }

        return Ok(new { groups, ungroupedCount });
    }

    [HttpGet("groups/{groupId:guid}/duplicate-submissions")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> DuplicateSubmissions(
        Guid groupId,
        [FromQuery] Guid? formId = null,
        CancellationToken ct = default)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(currentUserId, out var currentUserGuid);
        var isAdmin = User.IsInRole("Admin");

        var effectiveFormId = await smsInquiry.ResolveEffectiveFormIdForGroupFilterAsync(
            groupId, ungroupedOnly: false, formId, ct);
        if (effectiveFormId is not Guid fid || fid == Guid.Empty)
            return Ok(new { duplicateResponderCount = 0, duplicateSubmissionCount = 0, items = Array.Empty<object>() });

        var q = AuthorizedSubmissionsQuery(currentUserId, currentUserGuid, isAdmin);
        q = ResponderGroupSubmissionFilter.Apply(db, q, groupId, ungroupedOnly: false, effectiveFormId);
        q = q.Where(x => x.ResponderId != null);

        var rows = await (
            from s in q
            join r in db.Responders.AsNoTracking() on s.ResponderId equals r.Id
            orderby s.SubmittedAtUtc descending
            select new
            {
                s.Id,
                s.ResponderId,
                ResponderIdValue = s.ResponderId!.Value,
                ResponderName = r.FullName,
                ResponderMobile = r.MobileNumber,
                s.SubmitterName,
                s.SubmitterEmail,
                s.TrackingCode,
                s.SubmittedAtUtc,
                s.Status,
            }
        ).ToListAsync(ct);

        var items = rows
            .GroupBy(x => x.ResponderIdValue)
            .Where(g => g.Count() > 1)
            .Select(g =>
            {
                var list = g.OrderByDescending(x => x.SubmittedAtUtc).ToList();
                var first = list[0];
                return new
                {
                    responderId = g.Key,
                    fullName = first.ResponderName?.Trim()
                        ?? first.SubmitterName?.Trim()
                        ?? "بدون نام",
                    mobileNumber = first.ResponderMobile?.Trim()
                        ?? first.SubmitterEmail?.Trim()
                        ?? "",
                    submissionCount = list.Count,
                    submissions = list.Select(x => new
                    {
                        x.Id,
                        x.SubmittedAtUtc,
                        x.TrackingCode,
                        submitterName = x.SubmitterName,
                        approvalStatus = ToClientStatus(x.Status),
                    }).ToList(),
                };
            })
            .OrderBy(x => x.fullName)
            .ToList();

        return Ok(new
        {
            duplicateResponderCount = items.Count,
            duplicateSubmissionCount = items.Sum(x => x.submissionCount - 1),
            formId = fid,
            items,
        });
    }

    [HttpGet]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? fieldKey = null,
        [FromQuery] string? sortBy = "submitted_desc",
        [FromQuery] string? status = null,
        [FromQuery] Guid? groupId = null,
        [FromQuery] bool ungroupedOnly = false,
        [FromQuery] Guid? formId = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 50);
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        Guid.TryParse(currentUserId, out var currentUserGuid);
        var isAdmin = User.IsInRole("Admin");

        var q = AuthorizedSubmissionsQuery(currentUserId, currentUserGuid, isAdmin);

        var useFieldKeyFilter = !string.IsNullOrWhiteSpace(fieldKey)
            && !string.Equals(fieldKey.Trim(), FormSubmissionFieldSearchHelper.AllFieldsKey, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(search) && !useFieldKeyFilter)
        {
            var s = search.Trim();
            q = q.Where(x =>
                (x.SubmitterName ?? "").Contains(s) ||
                (x.SubmitterEmail ?? "").Contains(s) ||
                (x.TrackingCode ?? "").Contains(s) ||
                x.Form!.Title.Contains(s) ||
                (x.FieldsJson != null && x.FieldsJson.Contains(s)));
        }

        var effectiveFormId = await smsInquiry.ResolveEffectiveFormIdForGroupFilterAsync(
            groupId, ungroupedOnly, formId, ct);
        q = ResponderGroupSubmissionFilter.Apply(db, q, groupId, ungroupedOnly, effectiveFormId);

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

        if (page == 1)
            await ProcessDueScheduledWorkflowStartsAsync(ct);

        List<FormSubmission> data;
        int total;

        if (useFieldKeyFilter)
        {
            var allRows = await q.Include(x => x.Form).ToListAsync(ct);
            var filtered = allRows
                .Where(x => FormSubmissionFieldSearchHelper.Matches(x, search, fieldKey))
                .ToList();
            filtered = ApplySubmissionSort(filtered, sortBy);
            total = filtered.Count;
            data = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        }
        else
        {
            q = ApplySubmissionSortQuery(q, sortBy);
            total = await q.CountAsync(ct);
            data = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        }

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

        var formFieldList = await db.FormFields.AsNoTracking()
            .Where(ff => ff.FormId == submission.FormId)
            .Select(ff => new { ff.Id, ff.Label, ff.FieldType, ff.NestedFieldsJson })
            .ToListAsync(ct);

        var formFieldById = formFieldList.ToDictionary(x => x.Id);

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
            Fields = values.Select(v =>
            {
                var meta = v.FieldId is Guid fid && formFieldById.TryGetValue(fid, out var byId)
                    ? byId
                    : formFieldList.FirstOrDefault(x => string.Equals(x.Label, v.Label, StringComparison.Ordinal));
                var fieldType = meta is not null ? (int)meta.FieldType : 0;
                List<NestedFormFieldDto>? nestedFields = null;
                if (meta?.FieldType == FieldType.Repeatable && !string.IsNullOrWhiteSpace(meta.NestedFieldsJson))
                {
                    nestedFields = JsonSerializer.Deserialize<List<NestedFormFieldDto>>(meta.NestedFieldsJson);
                }

                return new
                {
                    v.Label,
                    v.Value,
                    FieldId = v.FieldId ?? meta?.Id,
                    FieldType = fieldType,
                    NestedFields = nestedFields,
                    IsFile = FormSubmissionUploadHelper.IsUploadPath(v.Value),
                    File = fileValues.FirstOrDefault(f =>
                        f.Url == FormSubmissionUploadHelper.NormalizeRelativePath(v.Value))
                };
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
            var effectiveFormId = await smsInquiry.ResolveEffectiveFormIdForGroupFilterAsync(
                req.GroupId, req.UngroupedOnly, null, ct);
            q = ResponderGroupSubmissionFilter.Apply(db, q, req.GroupId, req.UngroupedOnly, effectiveFormId);
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

    private static IQueryable<FormSubmission> ApplySubmissionSortQuery(IQueryable<FormSubmission> q, string? sortBy) =>
        sortBy switch
        {
            "submitted_asc" => q.OrderBy(x => x.SubmittedAtUtc),
            "name_asc" => q.OrderBy(x => x.SubmitterName),
            "name_desc" => q.OrderByDescending(x => x.SubmitterName),
            _ => q.OrderByDescending(x => x.SubmittedAtUtc),
        };

    private static List<FormSubmission> ApplySubmissionSort(List<FormSubmission> rows, string? sortBy) =>
        sortBy switch
        {
            "submitted_asc" => rows.OrderBy(x => x.SubmittedAtUtc).ToList(),
            "name_asc" => rows.OrderBy(x => x.SubmitterName).ToList(),
            "name_desc" => rows.OrderByDescending(x => x.SubmitterName).ToList(),
            _ => rows.OrderByDescending(x => x.SubmittedAtUtc).ToList(),
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
        foreach (var approvalLink in approvalLinks)
            approvalLink.IsActive = false;

        submission.IsDeleted = true;
        submission.DeletedAtUtc = DateTime.UtcNow;

        if (submission.DispatchLinkId is Guid linkId)
        {
            var dispatchLink = await db.FormDispatchLinks.FirstOrDefaultAsync(x => x.Id == linkId, ct);
            if (dispatchLink is not null)
                dispatchLink.UsedAtUtc = null;
        }

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
        var effectiveFormId = await smsInquiry.ResolveEffectiveFormIdForGroupFilterAsync(
            groupId, ungroupedOnly, formId, ct);
        var data = await wordTemplateService.GetGroupedSubmissionsAsync(
            effectiveFormId, groupId, ungroupedOnly, currentUserId, isAdmin, currentUserGuid, ct);
        return Ok(data);
    }

    private IQueryable<FormSubmission> AuthorizedSubmissionsQuery(
        string? currentUserId,
        Guid currentUserGuid,
        bool isAdmin)
    {
        var q = db.FormSubmissions
            .Include(x => x.Form)
            .Where(x => x.Form != null && !x.Form.IsDeleted)
            .Where(x => x.ResponderId == null
                || db.Responders.Any(r => r.Id == x.ResponderId.Value));

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

    [HttpGet("excel-export/options")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> GetExcelExportOptions(
        [FromQuery] Guid? groupId = null,
        [FromQuery] bool ungroupedOnly = false,
        CancellationToken ct = default)
    {
        try
        {
            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(currentUserId, out var currentUserGuid);
            var isAdmin = User.IsInRole("Admin");

            var options = await excelExportService.GetOptionsAsync(
                () => AuthorizedSubmissionsQuery(currentUserId, currentUserGuid, isAdmin),
                groupId,
                ungroupedOnly,
                ct);

            return Ok(options);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("excel-export-jobs")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> StartExcelExportJob(
        [FromBody] StartFormSubmissionExcelExportRequest req,
        CancellationToken ct)
    {
        try
        {
            if (req.SelectedFieldKeys is null || req.SelectedFieldKeys.Count == 0)
                return BadRequest(new { message = "حداقل یک فیلد برای خروجی انتخاب کنید" });

            var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
            Guid.TryParse(currentUserId, out var currentUserGuid);
            var isAdmin = User.IsInRole("Admin");
            Guid? userId = Guid.TryParse(currentUserId, out var uid) ? uid : null;

            var job = await excelExportService.CreateQueuedJobAsync(
                req.GroupId,
                req.UngroupedOnly,
                req.FormId,
                req.SelectedFieldKeys,
                userId,
                () => AuthorizedSubmissionsQuery(currentUserId, currentUserGuid, isAdmin),
                ct);

            var hangfireId = excelExportEnqueue.Enqueue(job.Id);
            await excelExportService.SetHangfireJobIdAsync(job.Id, hangfireId, ct);

            return Ok(new StartFormSubmissionExcelExportResponse(
                job.Id,
                "خروجی Excel در پس‌زمینه شروع شد — پس از اتمام پیام دانلود نمایش داده می‌شود"));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("excel-export-jobs/{jobId:guid}")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> GetExcelExportJobStatus(Guid jobId, CancellationToken ct)
    {
        var status = await excelExportService.GetStatusAsync(jobId, ct);
        return status is null ? NotFound(new { message = "کار یافت نشد" }) : Ok(status);
    }

    [HttpGet("excel-export-jobs/{jobId:guid}/download")]
    [Authorize(Policy = "responders.read")]
    public IActionResult DownloadExcelExportJob(Guid jobId)
    {
        var full = excelExportService.ResolveFileFullPath(jobId);
        if (full is null || !System.IO.File.Exists(full))
            return NotFound(new { message = "فایل Excel یافت نشد" });

        var fileName = Path.GetFileName(full);
        ContentDispositionHelper.SetAttachment(Response, fileName);
        return PhysicalFile(
            full,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
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

