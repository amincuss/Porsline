using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/user-groups")]
[Authorize]
public class AdminUserGroupsController(AppDbContext db) : ControllerBase
{
    private Guid? CurrentUserGuid
    {
        get
        {
            var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(raw, out var g) ? g : null;
        }
    }
    private bool CanReadAllUserGroups => User.HasClaim("permission", "usergroups.read.all");

    [HttpGet]
    [Authorize(Policy = "usergroups.read")]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);
        var query = db.UserGroups.Where(x => !x.IsDeleted).AsQueryable();
        if (!CanReadAllUserGroups)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue)
                return Ok(new { items = new List<object>(), total = 0, page, pageSize, totalPages = 0 });
            query = query.Where(x => x.CreatedByUserId == creatorId.Value);
        }
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
                MemberCount = x.Members.Count,
                LastMemberCreatedAtUtc = x.Members
                    .Join(db.Users, m => m.UserId, u => u.Id, (_, u) => (DateTime?)u.CreatedAtUtc)
                    .Max()
            })
            .ToListAsync(ct);
        return Ok(new { items, total, page, pageSize, totalPages = (int)Math.Ceiling((double)total / pageSize) });
    }

    [HttpGet("options")]
    [Authorize(Policy = "usergroups.read")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        var query = db.UserGroups
            .Where(x => !x.IsDeleted && x.IsActive)
            .AsQueryable();
        if (!CanReadAllUserGroups)
        {
            var creatorId = CurrentUserGuid;
            if (!creatorId.HasValue) return Ok(new List<object>());
            query = query.Where(x => x.CreatedByUserId == creatorId.Value);
        }
        var items = await query
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost]
    [Authorize(Policy = "usergroups.add")]
    public async Task<IActionResult> Create([FromBody] UserGroupUpsertDto dto, CancellationToken ct)
    {
        var name = dto.Name.Trim();
        if (name.Length < 2) return BadRequest(new { message = "نام گروه نامعتبر است" });
        if (await db.UserGroups.AnyAsync(x => x.Name == name && !x.IsDeleted, ct))
            return BadRequest(new { message = "این نام گروه قبلا ثبت شده است" });
        db.UserGroups.Add(new UserGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            IsActive = dto.IsActive,
            CreatedByUserId = CurrentUserGuid,
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه ثبت شد" });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "usergroups.update")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UserGroupUpsertDto dto, CancellationToken ct)
    {
        var item = await db.UserGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null || item.IsDeleted) return NotFound(new { message = "گروه یافت نشد" });
        var name = dto.Name.Trim();
        if (name.Length < 2) return BadRequest(new { message = "نام گروه نامعتبر است" });
        if (await db.UserGroups.AnyAsync(x => x.Name == name && x.Id != id && !x.IsDeleted, ct))
            return BadRequest(new { message = "این نام گروه قبلا ثبت شده است" });
        item.Name = name;
        item.IsActive = dto.IsActive;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه بروزرسانی شد" });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "usergroups.update")]
    public async Task<IActionResult> SoftDelete(Guid id, CancellationToken ct)
    {
        var item = await db.UserGroups.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item is null || item.IsDeleted) return NotFound(new { message = "گروه یافت نشد" });
        item.IsDeleted = true;
        item.IsActive = false;
        item.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "گروه حذف شد" });
    }
}

public record UserGroupUpsertDto(string Name, bool IsActive = true);
