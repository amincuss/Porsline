using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/public/form-actions")]
[AllowAnonymous]
public class PublicFormActionsController(
    AppDbContext db,
    FormActionLinkService actionLinks,
    FormPostApprovalService postApproval) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [HttpGet]
    public async Task<IActionResult> Access([FromQuery] string c, CancellationToken ct)
    {
        var link = await actionLinks.ResolveByCodeAsync(c, ct);
        if (link is null)
            return BadRequest(new { message = "لینک اقدام نامعتبر یا منقضی شده است" });

        var submission = link.FormSubmission;
        if (submission.Status != FormSubmissionStatus.Approved)
            return BadRequest(new { message = "این پاسخ هنوز تأیید نهایی نشده است" });

        var state = PostApprovalJsonHelper.DeserializeState(submission.PostApprovalJson);
        if (state is null || !state.AssigneeUserIds.Contains(link.AssigneeUserId))
            return BadRequest(new { message = "فاز اقدام برای شما تعریف نشده است" });

        var steps = string.IsNullOrWhiteSpace(submission.StepsJson)
            ? new List<ApprovalStepDto>()
            : FormWorkflowProcessor.DeserializeSteps(submission.StepsJson);

        await EnrichStepNamesAsync(steps, ct);

        var assigneeName = await ResolveUserDisplayNameAsync(link.AssigneeUserId, ct);
        var actionPhase = await FormActionPhaseHelper.BuildViewAsync(submission, db, ct);

        var submitterName = submission.SubmitterName?.Trim() ?? "";
        if (submission.DispatchLinkId is Guid dispatchId)
        {
            var dispatch = await db.FormDispatchLinks.AsNoTracking().FirstOrDefaultAsync(x => x.Id == dispatchId, ct);
            if (!string.IsNullOrWhiteSpace(dispatch?.ResponderFullName))
            {
                var responder = await db.Responders.AsNoTracking()
                    .FirstOrDefaultAsync(x => x.Id == dispatch.ResponderId, ct);
                submitterName = ResponderHonorific.FormatFullName(dispatch.ResponderFullName.Trim(), responder?.Gender);
            }
        }

        return Ok(new
        {
            submission.Id,
            submission.FormId,
            formTitle = submission.Form!.Title,
            submitterName,
            submission.SubmittedAtUtc,
            submission.WorkflowName,
            submission.TrackingCode,
            state.ActionDirectionLabel,
            state.Status,
            statusLabel = PostApprovalJsonHelper.StatusLabel(state.Status),
            state.Note,
            state.UpdatedAtUtc,
            state.CompletedAtUtc,
            assigneeUserId = link.AssigneeUserId,
            assigneeUserName = assigneeName,
            actionPhase,
            steps = steps.Select(s => new
            {
                s.Id,
                s.Order,
                s.UserId,
                s.UserName,
                userFirstName = s.UserFirstName,
                userLastName = s.UserLastName,
                userPositionTitle = s.UserPositionTitle,
                s.Status,
                s.Comment,
                s.ActionAt,
                s.OnReject,
                s.Note,
            }),
        });
    }

    [HttpPost("status")]
    public async Task<IActionResult> UpdateStatus([FromBody] PublicFormActionStatusRequest req, CancellationToken ct)
    {
        var link = await actionLinks.ResolveByCodeAsync(req.Code, ct);
        if (link is null)
            return BadRequest(new { message = "لینک اقدام نامعتبر یا منقضی شده است" });

        var (ok, err) = await postApproval.UpdateStatusAsync(
            link.FormSubmissionId,
            link.AssigneeUserId,
            req.Status,
            req.Note,
            ct);
        if (!ok) return BadRequest(new { message = err });
        return Ok(new { message = "وضعیت اقدام ذخیره شد" });
    }

    private async Task<string> ResolveUserDisplayNameAsync(Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId, ct);
        if (user is null) return "";
        var full = $"{user.FirstName} {user.LastName}".Trim();
        return string.IsNullOrWhiteSpace(full) ? (user.UserName ?? "") : full;
    }

    private async Task EnrichStepNamesAsync(List<ApprovalStepDto> steps, CancellationToken ct)
    {
        var ids = steps.Select(s => s.UserId).Distinct().ToList();
        var users = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName })
            .ToListAsync(ct);
        var map = users.ToDictionary(u => u.Id);
        foreach (var s in steps)
        {
            if (!map.TryGetValue(s.UserId, out var u)) continue;
            s.UserName = $"{u.FirstName} {u.LastName}".Trim();
        }
    }
}

public class PublicFormActionStatusRequest
{
    public string Code { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Note { get; set; }
}
