using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Api.Helpers;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/public")]
[AllowAnonymous]
public class PublicPortalController(
    PublicDocumentPortalService portal,
    AppDbContext db,
    IDocumentVersionFileAccess files) : ControllerBase
{
    private static readonly FileExtensionContentTypeProvider ContentTypes = new();

    [HttpGet("home")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Home(CancellationToken ct)
    {
        try
        {
            return Ok(await portal.GetHomeAsync(ct));
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(503, new { message = ex.Message });
        }
    }

    [HttpGet("branding")]
    [ResponseCache(Duration = 120, Location = ResponseCacheLocation.Any)]
    public async Task<IActionResult> Branding(CancellationToken ct) => Ok(await portal.GetBrandingAsync(ct));

    [HttpGet("documents")]
    public async Task<IActionResult> Documents([FromQuery] PublicDocumentListQuery q, CancellationToken ct) =>
        Ok(await portal.ListDocumentsAsync(q, ct));

    [HttpGet("documents/{slug}")]
    public async Task<IActionResult> DocumentBySlug(string slug, CancellationToken ct)
    {
        var detail = await portal.GetDocumentBySlugAsync(slug, ct);
        if (detail is null) return NotFound(new { message = "سند یافت نشد" });
        await portal.RecordViewAsync(detail.Id, GetVisitorKey(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
        return Ok(detail);
    }

    [HttpGet("categories")]
    public async Task<IActionResult> Categories(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var published = portal.PublishedProfilesQuery(now);
        var cats = await db.PublicCategories.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(ct);
        var result = new List<PublicCategoryListDto>();
        foreach (var c in cats)
        {
            var count = await published.CountAsync(p => p.PublicCategoryId == c.Id, ct);
            result.Add(new PublicCategoryListDto(c.Id, c.Name, c.Slug, c.Description, c.CoverImagePath, c.Icon, count));
        }
        return Ok(result);
    }

    [HttpGet("categories/{slug}")]
    public async Task<IActionResult> CategoryBySlug(string slug, [FromQuery] PublicDocumentListQuery q, CancellationToken ct)
    {
        var cat = await db.PublicCategories.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive, ct);
        if (cat is null) return NotFound(new { message = "دسته‌بندی یافت نشد" });
        q.CategorySlug = slug;
        var docs = await portal.ListDocumentsAsync(q, ct);
        return Ok(new { category = new PublicCategoryListDto(cat.Id, cat.Name, cat.Slug, cat.Description, cat.CoverImagePath, cat.Icon, docs.TotalCount), documents = docs });
    }

    [HttpGet("collections")]
    public async Task<IActionResult> Collections(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var published = portal.PublishedProfilesQuery(now);
        var cols = await db.PublicCollections.AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .ToListAsync(ct);
        var result = new List<PublicCollectionListDto>();
        foreach (var col in cols)
        {
            var count = await published.CountAsync(p => p.PublicCollectionId == col.Id, ct);
            result.Add(new PublicCollectionListDto(col.Id, col.Name, col.Slug, col.Description, col.CoverImagePath, col.Featured, count));
        }
        return Ok(result);
    }

    [HttpGet("collections/{slug}")]
    public async Task<IActionResult> CollectionBySlug(string slug, [FromQuery] PublicDocumentListQuery q, CancellationToken ct)
    {
        var col = await db.PublicCollections.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive, ct);
        if (col is null) return NotFound(new { message = "مجموعه یافت نشد" });
        q.CollectionSlug = slug;
        var docs = await portal.ListDocumentsAsync(q, ct);
        return Ok(new { collection = new PublicCollectionListDto(col.Id, col.Name, col.Slug, col.Description, col.CoverImagePath, col.Featured, docs.TotalCount), documents = docs });
    }

    [HttpGet("search")]
    public Task<IActionResult> Search([FromQuery] PublicDocumentListQuery q, CancellationToken ct) =>
        Documents(q, ct);

    [HttpGet("tags")]
    public async Task<IActionResult> Tags(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var docIds = portal.PublishedProfilesQuery(now).Select(p => p.DocumentId);
        var tags = await db.DocumentTags.AsNoTracking()
            .Where(t => docIds.Contains(t.DocumentId))
            .GroupBy(t => t.Tag)
            .Select(g => new { tag = g.Key, count = g.Count() })
            .OrderByDescending(x => x.count)
            .Take(100)
            .ToListAsync(ct);
        return Ok(tags);
    }

    [HttpGet("tags/{tag}")]
    public async Task<IActionResult> TagDocuments(string tag, [FromQuery] PublicDocumentListQuery q, CancellationToken ct)
    {
        q.Tag = tag;
        return await Documents(q, ct);
    }

    [HttpGet("download/{documentId:guid}")]
    public async Task<IActionResult> Download(Guid documentId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var profile = await db.DocumentPublicProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.DocumentId == documentId, ct);
        if (profile is null || !PublicDocumentPortalService.IsCurrentlyPublished(profile, now) || !profile.DownloadAllowed)
            return NotFound(new { message = "دانلود مجاز نیست" });

        var version = await portal.ResolvePublicVersionAsync(profile, ct);
        if (version is null) return NotFound(new { message = "فایل یافت نشد" });

        await portal.RecordDownloadAsync(documentId, GetVisitorKey(), HttpContext.Connection.RemoteIpAddress?.ToString(), ct);

        if (!ContentTypes.TryGetContentType(version.OriginalFileName, out var contentType))
            contentType = "application/octet-stream";
        var served = await DocumentVersionFileHttpHelper.TryServePhysicalAsync(
            files, version, contentType, version.OriginalFileName, inline: false, Response, ct);
        if (served is null)
            return NotFound(new { message = "فایل فیزیکی یافت نشد" });
        return served;
    }

    [HttpGet("preview/{slug}")]
    public async Task<IActionResult> Preview(string slug, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var profile = await portal.PublishedProfilesQuery(now).FirstOrDefaultAsync(p => p.Slug == slug, ct);
        if (profile is null || !profile.PreviewAvailable)
            return NotFound(new { message = "پیش‌نمایش در دسترس نیست" });

        var version = await portal.ResolvePublicVersionAsync(profile, ct);
        if (version is null) return NotFound(new { message = "فایل یافت نشد" });

        var ext = version.Extension.Trim().ToLowerInvariant();
        if (!ext.StartsWith('.')) ext = "." + ext;
        var previewable = ext is ".pdf" or ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".mp4" or ".webm" or ".mp3" or ".wav";
        if (!previewable) return BadRequest(new { message = "پیش‌نمایش برای این نوع فایل پشتیبانی نمی‌شود" });

        if (!ContentTypes.TryGetContentType(version.OriginalFileName, out var contentType))
            contentType = "application/octet-stream";
        var served = await DocumentVersionFileHttpHelper.TryServePhysicalAsync(
            files, version, contentType, version.OriginalFileName, inline: true, Response, ct);
        if (served is null)
            return NotFound(new { message = "فایل فیزیکی یافت نشد" });
        return served;
    }

    private string? GetVisitorKey()
    {
        if (Request.Headers.TryGetValue("X-Portal-Visitor", out var h) && !string.IsNullOrWhiteSpace(h))
            return h.ToString();
        if (Request.Cookies.TryGetValue("portal_visitor", out var c) && !string.IsNullOrWhiteSpace(c))
            return c;
        return null;
    }
}
