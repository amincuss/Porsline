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
[Route("api/public/contract-actions")]
[AllowAnonymous]
public class PublicContractActionsController(
    AppDbContext db,
    ContractActionLinkService actionLinks,
    ContractPostApprovalService postApproval) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    [HttpGet]
    public async Task<IActionResult> Access([FromQuery] string c, CancellationToken ct)
    {
        var link = await actionLinks.ResolveByCodeAsync(c, ct);
        if (link is null || link.ExpiresAtUtc < DateTime.UtcNow)
            return BadRequest(new { message = "لینک اقدام نامعتبر یا منقضی شده است" });

        var contract = link.Contract;
        if (contract.Status != ContractStatus.Approved)
            return BadRequest(new { message = "این قرارداد هنوز تأیید نهایی نشده است" });

        var state = PostApprovalJsonHelper.DeserializeState(contract.PostApprovalJson);
        if (state is null || !state.AssigneeUserIds.Contains(link.AssigneeUserId))
            return BadRequest(new { message = "فاز اقدام برای شما تعریف نشده است" });

        var steps = string.IsNullOrWhiteSpace(contract.StepsJson)
            ? new List<ApprovalStepDto>()
            : JsonSerializer.Deserialize<List<ApprovalStepDto>>(contract.StepsJson, JsonOpts) ?? new List<ApprovalStepDto>();

        await EnrichStepNamesAsync(steps, ct);

        var assigneeName = await ResolveUserDisplayNameAsync(link.AssigneeUserId, ct);
        var actionPhase = await ContractActionPhaseHelper.BuildViewAsync(contract, db, ct);
        var amendState = ContractAmendmentHelper.Deserialize(contract.AmendmentJson);
        var amendment = ContractAmendmentHelper.ToView(amendState, link.AssigneeUserId);

        return Ok(new
        {
            contract.Id,
            contract.ContractNumber,
            contract.Title,
            contract.SubjectPersonName,
            partyName = $"{contract.FirstName} {contract.LastName}".Trim(),
            state.ActionDirectionLabel,
            state.Status,
            statusLabel = PostApprovalJsonHelper.StatusLabel(state.Status),
            state.Note,
            state.UpdatedAtUtc,
            state.CompletedAtUtc,
            assigneeUserId = link.AssigneeUserId,
            assigneeUserName = assigneeName,
            steps,
            actionPhase,
            amendmentAssigneeName = assigneeName,
            amendment,
            workflowEvents = WorkflowEventHelper.ToViews(contract.WorkflowEventsJson),
        });
    }

    [HttpPost("status")]
    public async Task<IActionResult> UpdateStatus([FromBody] PublicContractActionStatusRequest req, CancellationToken ct)
    {
        var link = await actionLinks.ResolveByCodeAsync(req.Code, ct);
        if (link is null || link.ExpiresAtUtc < DateTime.UtcNow)
            return BadRequest(new { message = "لینک اقدام نامعتبر یا منقضی شده است" });

        var (ok, err) = await postApproval.UpdateStatusAsync(link.ContractId, link.AssigneeUserId, req.Status, req.Note, ct);
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

public class PublicContractActionStatusRequest
{
    public string Code { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Note { get; set; }
}
