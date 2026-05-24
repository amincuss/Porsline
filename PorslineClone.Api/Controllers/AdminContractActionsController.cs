using System.Security.Claims;

using System.Text.Json;

using Microsoft.AspNetCore.Authorization;

using Microsoft.AspNetCore.Mvc;

using Microsoft.EntityFrameworkCore;

using PorslineClone.Application.ContractTemplates;

using PorslineClone.Application.Contracts;

using PorslineClone.Domain.Entities;

using PorslineClone.Infrastructure.Persistence;

using PorslineClone.Infrastructure.Services;



namespace PorslineClone.Api.Controllers;



[ApiController]

[Route("api/admin/contract-actions")]

[Authorize]

public class AdminContractActionsController(

    AppDbContext db,

    ContractPostApprovalService postApproval) : ControllerBase

{

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };



    [HttpGet("directions")]

    [Authorize(Policy = "actions.read")]

    public IActionResult Directions() =>

        Ok(PostApprovalDirections.Items.Select(x => new { key = x.Key, label = x.Label }));



    [HttpGet("pending-count")]

    [Authorize(Policy = "actions.read")]

    public async Task<IActionResult> PendingCount(CancellationToken ct)

    {

        if (!TryGetUserId(out var userId)) return Unauthorized();



        var contracts = await db.Contracts

            .Where(c => c.Status == ContractStatus.Approved && !c.IsArchived)

            .Where(c => c.PostApprovalJson != null)

            .ToListAsync(ct);



        var linkedIds = await db.ContractActionLinks.AsNoTracking()

            .Where(l => l.AssigneeUserId == userId && l.IsActive)

            .Select(l => l.ContractId)

            .Distinct()

            .ToListAsync(ct);

        var known = contracts.Select(c => c.Id).ToHashSet();

        var extraIds = linkedIds.Where(id => !known.Contains(id)).ToList();

        if (extraIds.Count > 0)

        {

            var extra = await db.Contracts

                .Where(c => extraIds.Contains(c.Id) && c.Status == ContractStatus.Approved && !c.IsArchived)

                .ToListAsync(ct);

            contracts.AddRange(extra);

            foreach (var c in extra.Where(c => string.IsNullOrWhiteSpace(c.PostApprovalJson)))

                await postApproval.TryStartPostApprovalAsync(c, ct);

        }



        var count = 0;

        foreach (var c in contracts)

        {

            var state = PostApprovalJsonHelper.DeserializeState(c.PostApprovalJson);

            if (state is null || state.AssigneeUserIds.Count == 0) continue;

            if (!state.AssigneeUserIds.Contains(userId)) continue;

            if (string.Equals(state.Status, "completed", StringComparison.OrdinalIgnoreCase)) continue;

            count++;

        }



        return Ok(new { count });

    }



    [HttpGet]

    [Authorize(Policy = "actions.read")]

    public async Task<IActionResult> List(

        [FromQuery] string? q,

        [FromQuery] string? status,

        [FromQuery] string? view,

        [FromQuery] DateTime? fromUtc,

        [FromQuery] DateTime? toUtc,

        [FromQuery] string sort = "updated_desc",

        CancellationToken ct = default)

    {

        if (!TryGetUserId(out var userId)) return Unauthorized();



        var canReadAll = User.HasClaim("permission", "actions.read.all");

        var onlyCompleted = string.Equals(status?.Trim(), "completed", StringComparison.OrdinalIgnoreCase)

            || string.Equals(view?.Trim(), "completed", StringComparison.OrdinalIgnoreCase);

        var activeView = string.Equals(view?.Trim(), "active", StringComparison.OrdinalIgnoreCase)

            || (string.IsNullOrWhiteSpace(view) && string.IsNullOrWhiteSpace(status));



        var contracts = await db.Contracts

            .Where(c => c.Status == ContractStatus.Approved)

            .Where(c => onlyCompleted ? c.IsArchived : !c.IsArchived)

            .OrderByDescending(c => c.CreatedAtUtc)

            .ToListAsync(ct);



        if (!canReadAll)

        {

            var linkedIds = await db.ContractActionLinks.AsNoTracking()

                .Where(l => l.AssigneeUserId == userId && l.IsActive)

                .Select(l => l.ContractId)

                .Distinct()

                .ToListAsync(ct);

            var known = contracts.Select(c => c.Id).ToHashSet();

            var extraIds = linkedIds.Where(id => !known.Contains(id)).ToList();

            if (extraIds.Count > 0)

            {

                var extra = await db.Contracts

                    .Where(c => extraIds.Contains(c.Id) && c.Status == ContractStatus.Approved)

                    .Where(c => onlyCompleted ? c.IsArchived : !c.IsArchived)

                    .ToListAsync(ct);

                contracts.AddRange(extra);

            }

        }



        foreach (var c in contracts.Where(c => string.IsNullOrWhiteSpace(c.PostApprovalJson)))

            await postApproval.TryStartPostApprovalAsync(c, ct);



        var items = new List<ContractActionListItemDto>();

        foreach (var c in contracts)

        {

            var state = PostApprovalJsonHelper.DeserializeState(c.PostApprovalJson);

            if (state is null || state.AssigneeUserIds.Count == 0) continue;

            if (!canReadAll && !state.AssigneeUserIds.Contains(userId)) continue;



            if (activeView && string.Equals(state.Status, "completed", StringComparison.OrdinalIgnoreCase))

                continue;



            if (!string.IsNullOrWhiteSpace(status)

                && !string.Equals(state.Status, status.Trim(), StringComparison.OrdinalIgnoreCase))

                continue;



            var sortAt = state.UpdatedAtUtc ?? state.CompletedAtUtc ?? c.CreatedAtUtc;

            if (fromUtc.HasValue && sortAt < fromUtc.Value) continue;

            if (toUtc.HasValue && sortAt > toUtc.Value) continue;



            if (!string.IsNullOrWhiteSpace(q))

            {

                var term = q.Trim();

                if (!c.ContractNumber.Contains(term, StringComparison.OrdinalIgnoreCase)

                    && !c.Title.Contains(term, StringComparison.OrdinalIgnoreCase)

                    && !c.SubjectPersonName.Contains(term, StringComparison.OrdinalIgnoreCase)

                    && !state.ActionDirectionLabel.Contains(term, StringComparison.OrdinalIgnoreCase))

                    continue;

            }



            items.Add(new ContractActionListItemDto(

                c.Id,

                c.ContractNumber,

                c.Title,

                c.SubjectPersonName,

                state.ActionDirectionLabel,

                state.Status,

                PostApprovalJsonHelper.StatusLabel(state.Status),

                state.UpdatedAtUtc,

                state.CompletedAtUtc,

                c.CreatedAtUtc,

                c.WorkflowName));

        }



        items = sort switch

        {

            "updated_asc" => items.OrderBy(x => x.UpdatedAtUtc ?? x.ApprovedAtUtc).ToList(),

            "number" => items.OrderBy(x => x.ContractNumber).ToList(),

            _ => items.OrderByDescending(x => x.UpdatedAtUtc ?? x.ApprovedAtUtc).ToList(),

        };



        return Ok(new { items, total = items.Count });

    }



    [HttpGet("{id:guid}")]

    [Authorize(Policy = "actions.read")]

    public async Task<IActionResult> Get(Guid id, CancellationToken ct)

    {

        if (!TryGetUserId(out var userId)) return Unauthorized();



        var contract = await db.Contracts

            .Include(c => c.ContractType)

            .FirstOrDefaultAsync(c => c.Id == id, ct);

        if (contract is null) return NotFound(new { message = "قرارداد یافت نشد" });



        var state = PostApprovalJsonHelper.DeserializeState(contract.PostApprovalJson);

        if (state is null && contract.Status == ContractStatus.Approved)

        {

            await postApproval.TryStartPostApprovalAsync(contract, ct);

            state = PostApprovalJsonHelper.DeserializeState(contract.PostApprovalJson);

        }



        if (state is null || state.AssigneeUserIds.Count == 0)

            return NotFound(new { message = "فاز اقدام برای این قرارداد تعریف نشده است" });



        var canReadAll = User.HasClaim("permission", "actions.read.all");

        if (!canReadAll && !state.AssigneeUserIds.Contains(userId))

            return StatusCode(403, new { message = "این پرونده به شما ارجاع نشده است" });



        var steps = string.IsNullOrWhiteSpace(contract.StepsJson)

            ? new List<PorslineClone.Application.Contracts.ApprovalStepDto>()

            : JsonSerializer.Deserialize<List<PorslineClone.Application.Contracts.ApprovalStepDto>>(contract.StepsJson, JsonOpts)

              ?? new List<PorslineClone.Application.Contracts.ApprovalStepDto>();



        await EnrichStepNamesAsync(steps, ct);



        var assigneeNames = await ResolveUserNamesAsync(state.AssigneeUserIds, ct);

        var templateFieldValues = ParseTemplateFieldValues(contract.TemplateFieldValuesJson);



        return Ok(new ContractActionDetailDto(

            contract.Id,

            contract.ContractNumber,

            contract.Title,

            contract.SubjectPersonName,

            $"{contract.FirstName} {contract.LastName}".Trim(),

            contract.FirstName,

            contract.LastName,

            contract.NationalId,

            contract.Phone,

            contract.DateFromUtc,

            contract.DateToUtc,

            contract.ContractType?.Name,

            contract.WorkflowName,

            state.ActionDirectionLabel,

            state.Status,

            PostApprovalJsonHelper.StatusLabel(state.Status),

            state.Note,

            state.UpdatedAtUtc,

            state.CompletedAtUtc,

            state.UpdatedByUserName,

            assigneeNames,

            templateFieldValues,

            steps,

            state.AssigneeUserIds.Contains(userId)));

    }



    [HttpPatch("{id:guid}/status")]

    [Authorize(Policy = "actions.update")]

    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateContractActionStatusRequest req, CancellationToken ct)

    {

        if (!TryGetUserId(out var userId)) return Unauthorized();



        var (ok, err) = await postApproval.UpdateStatusAsync(id, userId, req.Status, req.Note, ct);

        if (!ok) return BadRequest(new { message = err });

        return Ok(new { message = "وضعیت اقدام ذخیره شد" });

    }



    private bool TryGetUserId(out Guid userId)

    {

        userId = default;

        return Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    }



    private static Dictionary<string, string>? ParseTemplateFieldValues(string? json)

    {

        if (string.IsNullOrWhiteSpace(json)) return null;

        try

        {

            return ContractTemplateFieldValuesParser.Parse(json);

        }

        catch

        {

            return null;

        }

    }



    private async Task<List<string>> ResolveUserNamesAsync(IReadOnlyList<Guid> userIds, CancellationToken ct)

    {

        if (userIds.Count == 0) return [];

        var users = await db.Users.AsNoTracking()

            .Where(u => userIds.Contains(u.Id))

            .Select(u => new { u.Id, u.FirstName, u.LastName, u.UserName })

            .ToListAsync(ct);

        return userIds

            .Select(id =>

            {

                var u = users.FirstOrDefault(x => x.Id == id);

                if (u is null) return "";

                var full = $"{u.FirstName} {u.LastName}".Trim();

                return string.IsNullOrWhiteSpace(full) ? (u.UserName ?? "") : full;

            })

            .Where(x => !string.IsNullOrWhiteSpace(x))

            .ToList();

    }



    private async Task EnrichStepNamesAsync(List<PorslineClone.Application.Contracts.ApprovalStepDto> steps, CancellationToken ct)

    {

        var ids = steps.Select(s => s.UserId).Distinct().ToList();

        var users = await db.Users.AsNoTracking()

            .Where(u => ids.Contains(u.Id))

            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Email })

            .ToListAsync(ct);

        var map = users.ToDictionary(u => u.Id);

        foreach (var s in steps)

        {

            if (!map.TryGetValue(s.UserId, out var u)) continue;

            s.UserName = $"{u.FirstName} {u.LastName}".Trim();

            s.UserEmail = u.Email;

        }

    }

}

