using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/user-settings")]
[Authorize]
public class AdminUserSettingsController(AppDbContext db) : ControllerBase
{
    [HttpGet("positions")]
    [Authorize(Policy = "settings.read")]
    public async Task<IActionResult> ListPositions([FromQuery] bool activeOnly = false, CancellationToken ct = default)
    {
        var q = db.UserPositions.AsQueryable();
        if (activeOnly) q = q.Where(x => x.IsActive);
        var items = await q
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Name)
            .Select(x => new UserPositionDto(x.Id, x.Name, x.SortOrder, x.IsActive))
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("positions")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> CreatePosition([FromBody] UpsertUserPositionRequest req, CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام سمت الزامی است" });
        if (await db.UserPositions.AnyAsync(x => x.Name == name, ct))
            return BadRequest(new { message = "این سمت قبلاً ثبت شده است" });

        var maxOrder = await db.UserPositions.MaxAsync(x => (int?)x.SortOrder, ct) ?? 0;
        var entity = new UserPosition
        {
            Id = Guid.NewGuid(),
            Name = name,
            SortOrder = req.SortOrder > 0 ? req.SortOrder : maxOrder + 1,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.UserPositions.Add(entity);
        await db.SaveChangesAsync(ct);
        return Ok(new UserPositionDto(entity.Id, entity.Name, entity.SortOrder, entity.IsActive));
    }

    [HttpPut("positions/{id:guid}")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> UpdatePosition(Guid id, [FromBody] UpsertUserPositionRequest req, CancellationToken ct)
    {
        var entity = await db.UserPositions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "سمت یافت نشد" });

        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            return BadRequest(new { message = "نام سمت الزامی است" });
        if (await db.UserPositions.AnyAsync(x => x.Name == name && x.Id != id, ct))
            return BadRequest(new { message = "این سمت قبلاً ثبت شده است" });

        entity.Name = name;
        if (req.SortOrder > 0) entity.SortOrder = req.SortOrder;
        if (req.IsActive.HasValue) entity.IsActive = req.IsActive.Value;
        await db.SaveChangesAsync(ct);
        return Ok(new UserPositionDto(entity.Id, entity.Name, entity.SortOrder, entity.IsActive));
    }

    [HttpDelete("positions/{id:guid}")]
    [Authorize(Policy = "settings.update")]
    public async Task<IActionResult> DeactivatePosition(Guid id, CancellationToken ct)
    {
        var entity = await db.UserPositions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null) return NotFound(new { message = "سمت یافت نشد" });
        entity.IsActive = false;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "سمت غیرفعال شد" });
    }
}

public record UserPositionDto(Guid Id, string Name, int SortOrder, bool IsActive);
public record UpsertUserPositionRequest(string Name, int SortOrder = 0, bool? IsActive = null);
