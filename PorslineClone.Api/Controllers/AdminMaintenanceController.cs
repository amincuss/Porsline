using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

/// <summary>اجرای دستی seed (فقط Admin) — وقتی Database:RunSeed روی سرور false است.</summary>
[ApiController]
[Route("api/admin/maintenance")]
[Authorize(Roles = "Admin")]
public class AdminMaintenanceController(
    AppDbContext db,
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager) : ControllerBase
{
    [HttpPost("run-seed")]
    public async Task<IActionResult> RunSeed(CancellationToken ct)
    {
        await DbSeeder.EnsureReferenceDataAsync(db, roleManager, ct);
        await DbSeeder.SeedAdminUserAsync(db, userManager);
        return Ok(new { message = "Seed با موفقیت اجرا شد. یک بار از پنل خارج و دوباره وارد شوید." });
    }
}
