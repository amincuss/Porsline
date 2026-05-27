using System.Security.Claims;
using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Application.Users;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/me")]
[Authorize]
public class MyAccountController(
    UserManager<AppUser> userManager,
    AppDbContext db,
    IWebHostEnvironment env) : ControllerBase
{
    private const long MaxAvatarBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedAvatarExt = [".jpg", ".jpeg", ".png", ".webp", ".gif"];

    private async Task<string?> BuildAvatarUrlAsync(AppUser user, CancellationToken ct = default)
    {
        var resolved = ProfileAvatarUrlHelper.Resolve(env.ContentRootPath, user.Id, user.AvatarUrl);
        if (resolved is null) return null;

        if (resolved.RepairedDbPath is not null &&
            !string.Equals(user.AvatarUrl, resolved.RepairedDbPath, StringComparison.OrdinalIgnoreCase))
        {
            user.AvatarUrl = resolved.RepairedDbPath;
            await userManager.UpdateAsync(user);
        }

        return ProfileAvatarUrlHelper.BuildPublicUrl(env.ContentRootPath, user.Id, user.AvatarUrl);
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
            AvatarUrl = await BuildAvatarUrlAsync(user, cancellationToken),
            HasSignature = !string.IsNullOrWhiteSpace(user.SignatureImagePath),
            SignatureDisplayDegree = user.SignatureDisplayDegree,
            SignatureWidthPx = UserSignatureDisplaySize.WidthPxFromDegree(user.SignatureDisplayDegree),
        });
    }

    [HttpGet("signature")]
    public async Task<IActionResult> GetMySignature()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId is null ? null : await userManager.FindByIdAsync(userId);
        if (user is null || string.IsNullOrWhiteSpace(user.SignatureImagePath)) return NotFound();
        if (!FormApprovalSignatureHelper.TryResolveSignatureFile(env, user.SignatureImagePath, out var fullPath))
            return NotFound();
        return PhysicalFile(fullPath, "image/png", enableRangeProcessing: true);
    }

    [HttpPost("signature")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [Consumes("multipart/form-data")]
    public IActionResult UploadMySignature([FromForm] UploadUserSignatureForm form)
    {
        _ = form;
        return StatusCode(403, new { message = "امضای دیجیتال فقط توسط مدیر سامانه قابل ثبت یا تغییر است" });
    }

    [HttpDelete("signature")]
    public IActionResult DeleteMySignature()
        => StatusCode(403, new { message = "امضای دیجیتال فقط توسط مدیر سامانه قابل ثبت یا تغییر است" });

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
            avatarUrl = await BuildAvatarUrlAsync(user, cancellationToken)
        });
    }

    [HttpGet("messages/stats")]
    public async Task<IActionResult> MessageStats(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var id)) return Unauthorized();

        var q = db.InboxMessages.Where(x => x.UserId == id);
        var total = await q.CountAsync(cancellationToken);
        var unread = await q.CountAsync(x => !x.IsRead && !x.IsArchived, cancellationToken);
        var archived = await q.CountAsync(x => x.IsArchived, cancellationToken);

        return Ok(new InboxStatsDto(total, unread, archived));
    }

    [HttpGet("messages")]
    public async Task<IActionResult> Messages([FromQuery] string? folder, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var id)) return Unauthorized();

        var q = db.InboxMessages.Where(x => x.UserId == id);
        q = (folder ?? "inbox").ToLowerInvariant() switch
        {
            "archived" => q.Where(x => x.IsArchived),
            "all" => q,
            _ => q.Where(x => !x.IsArchived),
        };

        var items = await q
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .Select(x => new InboxMessageDto(
                x.Id,
                x.Title,
                x.Body,
                x.IsRead,
                x.IsArchived,
                x.CreatedAtUtc,
                x.ReadAtUtc))
            .ToListAsync(cancellationToken);

        return Ok(items);
    }

    [HttpPatch("messages/{messageId:guid}/read")]
    public async Task<IActionResult> MarkMessageRead(Guid messageId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var id)) return Unauthorized();

        var msg = await db.InboxMessages.FirstOrDefaultAsync(x => x.Id == messageId && x.UserId == id, cancellationToken);
        if (msg is null) return NotFound(new { message = "پیام یافت نشد" });

        if (!msg.IsRead)
        {
            msg.IsRead = true;
            msg.ReadAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }

        return Ok(new { message = "پیام خوانده شد" });
    }

    [HttpPatch("messages/{messageId:guid}/archive")]
    public async Task<IActionResult> ArchiveMessage(Guid messageId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var id)) return Unauthorized();

        var msg = await db.InboxMessages.FirstOrDefaultAsync(x => x.Id == messageId && x.UserId == id, cancellationToken);
        if (msg is null) return NotFound(new { message = "پیام یافت نشد" });

        msg.IsArchived = true;
        if (!msg.IsRead)
        {
            msg.IsRead = true;
            msg.ReadAtUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "پیام بایگانی شد" });
    }

    [HttpDelete("messages/{messageId:guid}")]
    public async Task<IActionResult> DeleteMessage(Guid messageId, CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var id)) return Unauthorized();

        var msg = await db.InboxMessages.FirstOrDefaultAsync(x => x.Id == messageId && x.UserId == id, cancellationToken);
        if (msg is null) return NotFound(new { message = "پیام یافت نشد" });

        db.InboxMessages.Remove(msg);
        await db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "پیام حذف شد" });
    }

    private bool TryGetUserId(out Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out id);
    }
}

public sealed class UploadAvatarForm
{
    public IFormFile? Avatar { get; set; }
    public IFormFile? File { get; set; }
}

