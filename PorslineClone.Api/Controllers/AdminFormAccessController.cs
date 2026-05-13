using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/form-access")]
[Authorize]
public class AdminFormAccessController(AppDbContext db) : ControllerBase
{
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool CanReadAllFormAccess => User.IsInRole("Admin") || User.HasClaim("permission", "forms.access.read.all");

    private IQueryable<Domain.Entities.Form> ScopeFormsForAccess(IQueryable<Domain.Entities.Form> query)
    {
        if (CanReadAllFormAccess) return query;
        var userId = CurrentUserId;
        if (string.IsNullOrWhiteSpace(userId))
            return query.Where(_ => false);
        return query.Where(x => x.UserId == userId);
    }

    [HttpGet("users")]
    [Authorize(Policy = "forms.access.read")]
    public async Task<IActionResult> Users(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 50);

        var q = db.Users.Where(x => !x.IsSoftDeleted).AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            q = q.Where(x =>
                (x.FirstName + " " + x.LastName).Contains(s) ||
                (x.PhoneNumber ?? "").Contains(s));
        }

        var items = await q
            .OrderBy(x => x.FirstName)
            .ThenBy(x => x.LastName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                Name = (x.LastName + " " + x.FirstName).Trim(),
                Mobile = x.PhoneNumber ?? ""
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpGet("users/{userId:guid}/forms")]
    [Authorize(Policy = "forms.access.read")]
    public async Task<IActionResult> UserForms(Guid userId, [FromQuery] string? search, CancellationToken ct)
    {
        var assigned = await db.FormUserAccesses
            .Where(x => x.UserId == userId)
            .Select(x => x.FormId)
            .ToListAsync(ct);

        var forms = ScopeFormsForAccess(db.Forms).Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            forms = forms.Where(x => x.Title.Contains(s) || (x.Description ?? "").Contains(s));
        }

        var creatorIds = await forms
            .Select(x => x.UserId)
            .Where(x => x != null && x != "")
            .Distinct()
            .ToListAsync(ct);
        var creatorGuidIds = creatorIds
            .Select(x => Guid.TryParse(x, out var g) ? g : Guid.Empty)
            .Where(x => x != Guid.Empty)
            .Distinct()
            .ToList();
        var creators = await db.Users
            .Where(x => creatorGuidIds.Contains(x.Id))
            .Select(x => new { x.Id, Name = (x.LastName + " " + x.FirstName).Trim() })
            .ToListAsync(ct);
        var creatorMap = creators.ToDictionary(x => x.Id.ToString(), x => x.Name);

        var items = await forms
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.UserId,
                Assigned = assigned.Contains(x.Id)
            })
            .ToListAsync(ct);

        return Ok(items.Select(x => new
        {
            x.Id,
            x.Title,
            CreatorName = !string.IsNullOrWhiteSpace(x.UserId) && creatorMap.TryGetValue(x.UserId, out var name) ? name : "-",
            x.Assigned
        }));
    }

    [HttpPost("users/{userId:guid}/forms")]
    [Authorize(Policy = "forms.access.update")]
    public async Task<IActionResult> SetUserFormAccess(Guid userId, [FromBody] SetUserFormAccessDto dto, CancellationToken ct)
    {
        var userExists = await db.Users.AnyAsync(x => x.Id == userId && !x.IsSoftDeleted, ct);
        if (!userExists) return NotFound(new { message = "کاربر یافت نشد" });

        var form = await ScopeFormsForAccess(db.Forms).FirstOrDefaultAsync(x => x.Id == dto.FormId && !x.IsDeleted, ct);
        if (form is null) return NotFound(new { message = "فرم یافت نشد" });

        var current = await db.FormUserAccesses.FirstOrDefaultAsync(x => x.UserId == userId && x.FormId == dto.FormId, ct);
        if (dto.Assigned && current is null)
        {
            db.FormUserAccesses.Add(new PorslineClone.Domain.Entities.FormUserAccess
            {
                FormId = dto.FormId,
                UserId = userId,
                CreatedAtUtc = DateTime.UtcNow
            });
            await db.SaveChangesAsync(ct);
        }
        else if (!dto.Assigned && current is not null)
        {
            db.FormUserAccesses.Remove(current);
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { message = dto.Assigned ? "فرم به کاربر ارجاع شد" : "ارجاع فرم حذف شد" });
    }
}

public record SetUserFormAccessDto(Guid FormId, bool Assigned);
