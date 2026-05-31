using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/public/document-shares")]
public class PublicDocumentSharesController(
    AppDbContext db,
    DocumentFileStorageService storage) : ControllerBase
{
    [HttpGet("access")]
    public async Task<IActionResult> Access([FromQuery] string t, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(t))
            return BadRequest(new { message = "توکن اشتراک الزامی است" });

        var link = await db.DocumentShareLinks.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Token == t && !x.IsRevoked, ct);
        if (link is null) return NotFound(new { message = "لینک اشتراک نامعتبر است" });
        if (link.ExpiresAtUtc.HasValue && link.ExpiresAtUtc.Value < DateTime.UtcNow)
            return BadRequest(new { message = "لینک اشتراک منقضی شده است" });

        if (link.Scope == DocumentShareScope.OrganizationOnly && !TryGetUserId(out _))
            return Unauthorized(new { message = "برای مشاهده این لینک باید وارد شوید" });

        if (link.Scope == DocumentShareScope.SpecificUsers)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "برای مشاهده این لینک باید وارد شوید" });
            var allowed = ParseSpecificUsers(link.SpecificSubjectIdsJson);
            if (!allowed.Contains(userId))
                return Forbid();
        }

        if (link.ResourceType == DocumentNodeType.File)
        {
            var doc = await db.Documents.AsNoTracking()
                .Where(x => x.Id == link.ResourceId)
                .Select(x => new { x.Id, x.Title, x.Category, x.UpdatedAtUtc })
                .FirstOrDefaultAsync(ct);
            if (doc is null) return NotFound(new { message = "سند یافت نشد" });
            return Ok(new
            {
                resourceType = "file",
                doc.Id,
                doc.Title,
                doc.Category,
                doc.UpdatedAtUtc,
                download = $"{Request.Scheme}://{Request.Host}/api/public/document-shares/download?t={t}",
            });
        }

        var folder = await db.DocumentFolders.AsNoTracking()
            .Where(x => x.Id == link.ResourceId)
            .Select(x => new { x.Id, x.Name, x.CreatedAtUtc })
            .FirstOrDefaultAsync(ct);
        if (folder is null) return NotFound(new { message = "پوشه یافت نشد" });
        return Ok(new
        {
            resourceType = "folder",
            folder.Id,
            folder.Name,
            folder.CreatedAtUtc,
        });
    }

    [HttpGet("download")]
    public async Task<IActionResult> Download([FromQuery] string t, CancellationToken ct)
    {
        var link = await db.DocumentShareLinks.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Token == t && !x.IsRevoked, ct);
        if (link is null) return NotFound(new { message = "لینک اشتراک نامعتبر است" });
        if (link.ResourceType != DocumentNodeType.File)
            return BadRequest(new { message = "این لینک برای پوشه است و فایل ندارد" });
        if (link.ExpiresAtUtc.HasValue && link.ExpiresAtUtc.Value < DateTime.UtcNow)
            return BadRequest(new { message = "لینک اشتراک منقضی شده است" });
        if (link.Scope == DocumentShareScope.OrganizationOnly && !TryGetUserId(out _))
            return Unauthorized(new { message = "برای دانلود باید وارد شوید" });
        if (link.Scope == DocumentShareScope.SpecificUsers)
        {
            if (!TryGetUserId(out var userId))
                return Unauthorized(new { message = "برای دانلود باید وارد شوید" });
            var allowed = ParseSpecificUsers(link.SpecificSubjectIdsJson);
            if (!allowed.Contains(userId))
                return Forbid();
        }

        var latest = await db.DocumentVersions.AsNoTracking()
            .Where(x => x.DocumentId == link.ResourceId)
            .OrderByDescending(x => x.VersionNumber)
            .FirstOrDefaultAsync(ct);
        if (latest is null) return NotFound(new { message = "نسخه فایل یافت نشد" });
        var fullPath = storage.ResolveFullPath(latest.StoredPath);
        if (!System.IO.File.Exists(fullPath)) return NotFound(new { message = "فایل فیزیکی یافت نشد" });
        return PhysicalFile(fullPath, "application/octet-stream", latest.OriginalFileName);
    }

    private static HashSet<Guid> ParseSpecificUsers(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var ids = JsonSerializer.Deserialize<List<Guid>>(json) ?? [];
            return ids.ToHashSet();
        }
        catch
        {
            return [];
        }
    }

    private bool TryGetUserId(out Guid id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(userId, out id);
    }
}
