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
[Route("api/admin/contract-settings")]
[Authorize]
public class AdminContractSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet("workflow")]
    [Authorize(Policy = "contracts.settings.read")]
    public async Task<IActionResult> GetWorkflow(CancellationToken ct)
    {
        var settings = await db.ContractSettings.FirstOrDefaultAsync(x => x.Id == 1, ct)
            ?? new ContractSettings { Id = 1, ApprovalEnabled = false };
        var steps = string.IsNullOrWhiteSpace(settings.ApprovalWorkflowJson)
            ? new List<WorkflowStepDto>()
            : (JsonSerializer.Deserialize<List<WorkflowStepDto>>(settings.ApprovalWorkflowJson) ?? []);
        return Ok(new WorkflowSettingsDto(settings.ApprovalEnabled, steps.OrderBy(x => x.Order).ToList()));
    }

    [HttpPut("workflow")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> SaveWorkflow([FromBody] SaveWorkflowRequest req, CancellationToken ct)
    {
        var settings = await db.ContractSettings.FirstOrDefaultAsync(x => x.Id == 1, ct);
        if (settings is null)
        {
            settings = new ContractSettings { Id = 1 };
            db.ContractSettings.Add(settings);
        }

        var cleaned = req.Steps
            .Where(x => x.UserId != Guid.Empty)
            .OrderBy(x => x.Order)
            .Select((x, i) => x with { Order = i + 1, OnReject = x.OnReject is "continue" ? "continue" : "stop" })
            .ToList();

        if (req.Enabled && cleaned.Count > 0)
        {
            var signatureErr = await WorkflowUserSignatureValidator.ValidateUserIdsAsync(
                db,
                cleaned.Select(s => s.UserId),
                ct);
            if (signatureErr is not null)
                return BadRequest(new { message = signatureErr });
        }

        settings.ApprovalEnabled = req.Enabled;
        settings.ApprovalWorkflowJson = req.Enabled ? JsonSerializer.Serialize(cleaned) : null;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گردش تأیید قرارداد ذخیره شد" });
    }

    [HttpGet("types")]
    [Authorize(Policy = "contracts.settings.read")]
    public async Task<IActionResult> ListTypes(CancellationToken ct)
    {
        var items = await db.ContractTypes
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new ContractTypeDto(x.Id, x.Name, x.SortOrder, x.IsActive))
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("types")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> CreateType([FromBody] UpsertContractTypeRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام نوع قرارداد الزامی است" });
        if (await db.ContractTypes.AnyAsync(x => x.Name == name, ct))
            return BadRequest(new { message = "این نام قبلاً ثبت شده است" });

        var maxOrder = await db.ContractTypes.MaxAsync(x => (int?)x.SortOrder, ct) ?? 0;
        var entity = new ContractType
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = req.SortOrder > 0 ? req.SortOrder : maxOrder + 1,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ContractTypes.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(new ContractTypeDto(entity.Id, entity.Name, entity.SortOrder, entity.IsActive));
    }

    [HttpPut("types/{id:guid}")]
    [Authorize(Policy = "contracts.settings.update")]
    public async Task<IActionResult> UpdateType(Guid id, [FromBody] UpsertContractTypeRequest req, CancellationToken ct)
    {
        var entity = await db.ContractTypes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "نوع قرارداد یافت نشد" });

        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام نوع قرارداد الزامی است" });
        if (await db.ContractTypes.AnyAsync(x => x.Name == name && x.Id != id, ct))
            return BadRequest(new { message = "این نام قبلاً ثبت شده است" });

        entity.Name = name;
        if (req.SortOrder > 0) entity.SortOrder = req.SortOrder;
        if (req.IsActive.HasValue) entity.IsActive = req.IsActive.Value;
        await db.SaveChangesAsync(ct);
        return Ok(new ContractTypeDto(entity.Id, entity.Name, entity.SortOrder, entity.IsActive));
    }

    [HttpDelete("types/{id:guid}")]
    [Authorize(Policy = "contracts.settings.delete")]
    public async Task<IActionResult> DeactivateType(Guid id, CancellationToken ct)
    {
        var entity = await db.ContractTypes.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "نوع قرارداد یافت نشد" });
        entity.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "نوع قرارداد غیرفعال شد" });
    }
}

public record ContractTypeDto(Guid Id, string Name, int SortOrder, bool IsActive);
public record UpsertContractTypeRequest(string Name, int SortOrder = 0, bool? IsActive = null);
