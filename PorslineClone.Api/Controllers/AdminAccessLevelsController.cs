using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/access-levels")]
[Authorize]
public class AdminAccessLevelsController(AppDbContext db, RoleManager<AppRole> roleManager) : ControllerBase
{
    [HttpPost("roles")]
    [Authorize(Policy = "roles.update")]
    public async Task<IActionResult> CreateRole([FromBody] CreateRoleDto dto)
    {
        var normalizedName = dto.Name.Trim();
        var exists = await roleManager.FindByNameAsync(normalizedName);
        if (exists is not null) return Conflict(new { message = "این نقش قبلا ثبت شده است" });

        var create = await roleManager.CreateAsync(new AppRole
        {
            Id = Guid.NewGuid(),
            Name = normalizedName,
            NormalizedName = normalizedName.ToUpperInvariant(),
            DisplayName = dto.DisplayName.Trim()
        });

        if (!create.Succeeded)
            return BadRequest(create.Errors.Select(e => e.Description));

        return Ok(new { message = "نقش با موفقیت ایجاد شد" });
    }

    [HttpGet("roles")]
    [Authorize(Policy = "roles.read")]
    public async Task<IActionResult> Roles(CancellationToken cancellationToken)
    {
        var items = await db.Roles
            .OrderBy(x => x.Name)
            .Select(x => new RoleItemDto(x.Id, x.Name!, x.DisplayName))
            .ToListAsync(cancellationToken);
        return Ok(items);
    }

    [HttpGet("roles/{roleName}/permissions")]
    [Authorize(Policy = "roles.read")]
    public async Task<IActionResult> RolePermissions(string roleName, CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null) return NotFound(new { message = "نقش پیدا نشد" });

        var assigned = await db.RolePermissions
            .Where(x => x.RoleId == role.Id)
            .Select(x => x.PermissionId)
            .ToListAsync(cancellationToken);

        var permissionsRaw = await db.Permissions
            .Select(x => new RolePermissionItemDto(x.Id, x.Name, assigned.Contains(x.Id)))
            .ToListAsync(cancellationToken);

        // Defensive dedupe: if legacy data has duplicated permission names,
        // keep only one row per permission name in access-level UI.
        var permissions = permissionsRaw
            .GroupBy(x => x.PermissionName, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var anyAssigned = g.Any(x => x.Assigned);
                var first = g.First();
                return first with { Assigned = anyAssigned };
            })
            .ToList();

        var ordered = permissions
            .OrderByDescending(x => x.Assigned)
            .ThenBy(x => x.PermissionName)
            .ToList();

        return Ok(ordered);
    }

    [HttpPost("roles/{roleName}/permissions")]
    [Authorize(Policy = "roles.update")]
    public async Task<IActionResult> SetRolePermission(string roleName, [FromBody] SetRolePermissionDto dto, CancellationToken cancellationToken)
    {
        var role = await roleManager.FindByNameAsync(roleName);
        if (role is null) return NotFound(new { message = "نقش پیدا نشد" });

        var permission = await db.Permissions.FirstOrDefaultAsync(x => x.Name == dto.PermissionName, cancellationToken);
        if (permission is null) return NotFound(new { message = "پرمیژن پیدا نشد" });

        var current = await db.RolePermissions
            .FirstOrDefaultAsync(x => x.RoleId == role.Id && x.PermissionId == permission.Id, cancellationToken);

        if (dto.Assigned && current is null)
        {
            db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permission.Id });
            await db.SaveChangesAsync(cancellationToken);
        }
        else if (!dto.Assigned && current is not null)
        {
            db.RolePermissions.Remove(current);
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { message = dto.Assigned ? "دسترسی اختصاص داده شد" : "دسترسی حذف شد" });
    }
}
