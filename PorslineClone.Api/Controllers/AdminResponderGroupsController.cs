using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/responder-groups")]
[Authorize]
public class AdminResponderGroupsController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.ResponderGroups.Where(x => !x.IsDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var q = search.Trim();
            query = query.Where(x => x.Name.Contains(q));
        }

        query = sortBy switch
        {
            "name_asc" => query.OrderBy(x => x.Name),
            "name_desc" => query.OrderByDescending(x => x.Name),
            "created_asc" => query.OrderBy(x => x.CreatedAtUtc),
            _ => query.OrderByDescending(x => x.CreatedAtUtc),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.IsActive,
                x.CreatedAtUtc,
                MemberCount = x.Members.Count
            })
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling((double)total / pageSize) });
    }

    [HttpGet("options")]
    [Authorize(Policy = "responders.read")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        var items = await db.ResponderGroups
            .Where(x => !x.IsDeleted && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = "responders.add")]
    public async Task<IActionResult> Create([FromBody] ResponderGroupUpsertDto dto, CancellationToken ct)
    {
        var name = dto.Name.Trim();
        if (name.Length < 2) return BadRequest(new { message = "نام گروه نامعتبر است" });
        if (await db.ResponderGroups.AnyAsync(x => x.Name == name && !x.IsDeleted, ct))
            return BadRequest(new { message = "این نام گروه قبلا ثبت شده است" });

        db.ResponderGroups.Add(new ResponderGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = dto.IsActive,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه ثبت شد" });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "responders.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ResponderGroupUpsertDto dto, CancellationToken ct)
    {
        var item = await db.ResponderGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null || item.IsDeleted) return NotFound(new { message = "گروه یافت نشد" });
        var name = dto.Name.Trim();
        if (name.Length < 2) return BadRequest(new { message = "نام گروه نامعتبر است" });
        if (await db.ResponderGroups.AnyAsync(x => x.Name == name && x.Id != id && !x.IsDeleted, ct))
            return BadRequest(new { message = "این نام گروه قبلا ثبت شده است" });
        item.Name = name;
        item.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه بروزرسانی شد" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "responders.update")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        var item = await db.ResponderGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null || item.IsDeleted) return NotFound(new { message = "گروه یافت نشد" });
        item.IsDeleted = true;
        item.IsActive = false;
        item.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه حذف شد" });
    }
}

public record ResponderGroupUpsertDto(string Name, bool IsActive = true);
