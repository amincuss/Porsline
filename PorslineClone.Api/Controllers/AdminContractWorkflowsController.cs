using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/contract-workflows")]
[Authorize]
public class AdminContractWorkflowsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "contracts.settings.read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.ContractWorkflowTemplates
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        var items = rows.Select(x => new ContractWorkflowTemplateListItemDto(
            x.Id,
            x.Name,
            DeserializeSteps(x.StepsJson).Count,
            x.IsActive,
            x.CreatedAtUtc)).ToList();
        return Ok(items);
    }

    [HttpGet("active")]
    [Authorize(Policy = "contracts.read")]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        var rows = await db.ContractWorkflowTemplates
            .Where(x => x.IsActive)
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        var items = rows.Select(x => new
        {
            x.Id,
            x.Name,
            approverCount = DeserializeSteps(x.StepsJson).Count,
        }).ToList();
        return Ok(items);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "contracts.settings.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var x = await db.ContractWorkflowTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (x is null) return NotFound(new { message = "گردش یافت نشد" });

        var steps = DeserializeSteps(x.StepsJson);
        return Ok(new ContractWorkflowTemplateDetailDto(x.Id, x.Name, x.IsActive, steps));
    }

    [HttpPost]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> Create([FromBody] SaveWorkflowTemplateRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام گردش الزامی است" });
        if (await db.ContractWorkflowTemplates.AnyAsync(x => x.Name == name, ct))
            return BadRequest(new { message = "این نام گردش قبلاً ثبت شده است" });

        var cleaned = CleanSteps(req.Steps);
        if (cleaned.Count == 0)
            return BadRequest(new { message = "حداقل یک مرحله تأیید لازم است" });

        Guid? userId = null;
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            userId = uid;

        var entity = new ContractWorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            StepsJson = JsonSerializer.Serialize(cleaned),
            IsActive = true,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ContractWorkflowTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(new ContractWorkflowTemplateDetailDto(entity.Id, entity.Name, entity.IsActive, cleaned));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveWorkflowTemplateRequest req, CancellationToken ct)
    {
        var entity = await db.ContractWorkflowTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "گردش یافت نشد" });

        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام گردش الزامی است" });
        if (await db.ContractWorkflowTemplates.AnyAsync(x => x.Name == name && x.Id != id, ct))
            return BadRequest(new { message = "این نام گردش قبلاً ثبت شده است" });

        var cleaned = CleanSteps(req.Steps);
        if (cleaned.Count == 0)
            return BadRequest(new { message = "حداقل یک مرحله تأیید لازم است" });

        entity.Name = name;
        entity.StepsJson = JsonSerializer.Serialize(cleaned);
        await db.SaveChangesAsync(ct);
        return Ok(new ContractWorkflowTemplateDetailDto(entity.Id, entity.Name, entity.IsActive, cleaned));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var entity = await db.ContractWorkflowTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "گردش یافت نشد" });
        entity.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گردش غیرفعال شد" });
    }

    private static List<PorslineClone.Application.Contracts.WorkflowStepDto> CleanSteps(List<PorslineClone.Application.Contracts.WorkflowStepDto> steps) =>
        steps
            .Where(x => x.UserId != Guid.Empty)
            .OrderBy(x => x.Order)
            .Select((x, i) => x with
            {
                Order = i + 1,
                OnReject = x.OnReject is "continue" ? "continue" : "stop"
            })
            .ToList();

    private static List<PorslineClone.Application.Contracts.WorkflowStepDto> DeserializeSteps(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : (JsonSerializer.Deserialize<List<PorslineClone.Application.Contracts.WorkflowStepDto>>(json) ?? []);

    private static int CountSteps(string? json) => DeserializeSteps(json).Count;
}
