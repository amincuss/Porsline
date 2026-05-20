using System.Security.Claims;
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
[Route("api/admin/form-workflows")]
[Authorize]
public class AdminFormWorkflowsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "forms.rules.read")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.FormWorkflowTemplates
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        var items = rows.Select(x => new FormWorkflowTemplateListItemDto(
            x.Id,
            x.Name,
            DeserializeSteps(x.StepsJson).Count,
            x.IsActive,
            x.CreatedAtUtc)).ToList();
        return Ok(items);
    }

    [HttpGet("active")]
    [Authorize(Policy = "forms.read")]
    public async Task<IActionResult> Active(CancellationToken ct)
    {
        var rows = await db.FormWorkflowTemplates
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
    [Authorize(Policy = "forms.rules.read")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var x = await db.FormWorkflowTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (x is null || !x.IsActive) return NotFound(new { message = "گردش یافت نشد یا حذف شده است" });

        var steps = DeserializeSteps(x.StepsJson);
        return Ok(new FormWorkflowTemplateDetailDto(x.Id, x.Name, x.IsActive, steps));
    }

    [HttpPost]
    [Authorize(Policy = "forms.rules.update")]
    public async Task<IActionResult> Create([FromBody] SaveWorkflowTemplateRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام گردش الزامی است" });
        if (await db.FormWorkflowTemplates.AnyAsync(x => x.Name == name && x.IsActive, ct))
            return BadRequest(new { message = "این نام گردش قبلاً ثبت شده است" });

        var cleaned = CleanSteps(req.Steps);
        if (cleaned.Count == 0)
            return BadRequest(new { message = "حداقل یک مرحله تأیید لازم است" });

        Guid? userId = null;
        if (Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid))
            userId = uid;

        var entity = new FormWorkflowTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            StepsJson = JsonSerializer.Serialize(cleaned),
            IsActive = true,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.FormWorkflowTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(new FormWorkflowTemplateDetailDto(entity.Id, entity.Name, entity.IsActive, cleaned));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "forms.rules.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] SaveWorkflowTemplateRequest req, CancellationToken ct)
    {
        var entity = await db.FormWorkflowTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "گردش یافت نشد" });
        if (!entity.IsActive) return BadRequest(new { message = "این گردش حذف شده و قابل ویرایش نیست" });

        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام گردش الزامی است" });
        if (await db.FormWorkflowTemplates.AnyAsync(x => x.Name == name && x.Id != id && x.IsActive, ct))
            return BadRequest(new { message = "این نام گردش قبلاً ثبت شده است" });

        var cleaned = CleanSteps(req.Steps);
        if (cleaned.Count == 0)
            return BadRequest(new { message = "حداقل یک مرحله تأیید لازم است" });

        entity.Name = name;
        entity.StepsJson = JsonSerializer.Serialize(cleaned);
        await db.SaveChangesAsync(ct);
        return Ok(new FormWorkflowTemplateDetailDto(entity.Id, entity.Name, entity.IsActive, cleaned));
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "forms.rules.update")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var entity = await db.FormWorkflowTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "گردش یافت نشد" });

        var formsUsage = await WorkflowTemplateSoftDelete.CountFormUsageAsync(db, id, ct);
        var submissionsUsage = await WorkflowTemplateSoftDelete.CountFormSubmissionUsageAsync(db, id, ct);
        if (entity.IsActive)
        {
            entity.IsActive = false;
            await db.SaveChangesAsync(ct);
        }

        var usage = new WorkflowTemplateSoftDelete.UsageCounts(0, formsUsage, submissionsUsage);
        var message = WorkflowTemplateSoftDelete.BuildDeleteMessage(entity.Name, usage, isFormWorkflow: true);
        return Ok(new { message, formsUsage, formSubmissionsUsage = submissionsUsage, isActive = false });
    }

    private static List<WorkflowStepDto> CleanSteps(List<WorkflowStepDto> steps) =>
        steps
            .Where(x => x.UserId != Guid.Empty)
            .OrderBy(x => x.Order)
            .Select((x, i) => x with
            {
                Order = i + 1,
                OnReject = x.OnReject is "continue" ? "continue" : "stop"
            })
            .ToList();

    private static List<WorkflowStepDto> DeserializeSteps(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : (JsonSerializer.Deserialize<List<WorkflowStepDto>>(json) ?? []);
}
