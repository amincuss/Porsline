using System.Security.Claims;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MyAccountController(UserManager<AppUser> userManager, AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    private const long MaxAvatarBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAvatarExt = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private string? BuildAvatarUrl(string? avatarPath)
    {
        if (string.IsNullOrWhiteSpace(avatarPath)) return null;
        var relative = avatarPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.Combine(env.ContentRootPath, relative);
        var version = System.IO.File.Exists(fullPath)
            ? System.IO.File.GetLastWriteTimeUtc(fullPath).Ticks
            : DateTime.UtcNow.Ticks;
        return $"{avatarPath}?v={version}";
    }

    [HttpGet("profile")]
    public async Task<IActionResult> Profile(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        return Ok(new
        {
            user.Id,
            user.FirstName,
            user.LastName,
            MobileNumber = user.PhoneNumber ?? "",
            user.NationalCode,
            user.AboutMe,
            AvatarUrl = BuildAvatarUrl(user.AvatarUrl)
        });
    }

    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);
        if (user is null) return Unauthorized();

        user.AboutMe = dto.AboutMe;
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
            return BadRequest(new { message = "ذخیره پروفایل ناموفق بود" });
        return Ok(new { message = "پروفایل ذخیره شد" });
    }

    [HttpPost("profile/avatar")]
    [RequestSizeLimit(MaxAvatarBytes + 1024)]
    [Consumes("multipart/form-data")]
    [Produces("application/json")]
    public async Task<IActionResult> UploadAvatar([FromForm] UploadAvatarForm formData, CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);
        var file = formData.Avatar ?? formData.File;
        if (file is null && Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            file = form.Files.GetFile("avatar") ?? form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
        }
        if (user is null) return Unauthorized();
        if (file is null || file.Length <= 0) return BadRequest(new { message = "فایل تصویر ارسال نشده است" });
        if (file.Length > MaxAvatarBytes) return BadRequest(new { message = "حداکثر حجم تصویر 5MB است" });

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedAvatarExt.Contains(ext))
            return BadRequest(new { message = "فرمت تصویر مجاز نیست. فقط JPG/PNG/WEBP/GIF قابل قبول است" });

        var expertFolder = Path.Combine(env.ContentRootPath, "ProfileImages", user.Id.ToString(), "profile");
        Directory.CreateDirectory(expertFolder);

        var newName = $"avatar_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
        var newPath = Path.Combine(expertFolder, newName);
        await using (var fs = System.IO.File.Create(newPath))
        {
            await file.CopyToAsync(fs, cancellationToken);
        }

        // Keep only the latest profile image for this expert.
        foreach (var oldFile in Directory.EnumerateFiles(expertFolder))
        {
            if (!string.Equals(oldFile, newPath, StringComparison.OrdinalIgnoreCase))
                System.IO.File.Delete(oldFile);
        }

        if (!string.IsNullOrWhiteSpace(user.AvatarUrl))
        {
            var oldPath = Path.Combine(env.ContentRootPath, user.AvatarUrl.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            if (System.IO.File.Exists(oldPath) && !string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase))
                System.IO.File.Delete(oldPath);
        }

        user.AvatarUrl = $"/ProfileImages/{user.Id}/profile/{newName}";
        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
            return BadRequest(new { message = "ذخیره مسیر عکس پروفایل ناموفق بود" });

        return Ok(new
        {
            message = "عکس پروفایل با موفقیت ذخیره شد",
            avatarUrl = BuildAvatarUrl(user.AvatarUrl)
        });
    }

    [HttpGet("messages")]
    public async Task<IActionResult> Messages(CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userId, out var id)) return Unauthorized();

        var items = await db.InboxMessages
            .Where(x => x.UserId == id)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(50)
            .Select(x => new InboxMessageDto(x.Id, x.Title, x.Body, x.IsRead, x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }
}

public sealed class UploadAvatarForm
{
    public IFormFile? Avatar { get; set; }
    public IFormFile? File { get; set; }
}

