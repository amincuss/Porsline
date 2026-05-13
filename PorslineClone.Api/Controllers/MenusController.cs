using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/menus")]
[Authorize]
public class MenusController(AppDbContext db, UserManager<AppUser> userManager) : ControllerBase
{
    [HttpGet("my")]
    public async Task<IActionResult> My(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        var roleNames = await userManager.GetRolesAsync(user);
        var roleIds = await db.Roles.Where(r => roleNames.Contains(r.Name!)).Select(r => r.Id).ToListAsync(cancellationToken);
        var menuIds = await db.RoleMenus.Where(rm => roleIds.Contains(rm.RoleId)).Select(rm => rm.MenuId).Distinct().ToListAsync(cancellationToken);
        var menus = await db.MenuItems.Where(m => menuIds.Contains(m.Id)).OrderBy(m => m.Order).ToListAsync(cancellationToken);

        var map = menus.Select(m => new MenuDto { Id = m.Id, Title = m.Title, Icon = m.Icon, IconColor = m.IconColor, Route = m.Route }).ToDictionary(x => x.Id);
        var roots = new List<MenuDto>();

        foreach (var item in menus)
        {
            var dto = map[item.Id];
            if (item.ParentId is { } pid && map.TryGetValue(pid, out var p)) p.Children.Add(dto); else roots.Add(dto);
        }

        return Ok(roots);
    }
}
