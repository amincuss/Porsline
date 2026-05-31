using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/document-public-portal")]
[Authorize(Policy = "forms.update")]
public class AdminDocumentPublicPortalController(
    AppDbContext db,
    PublicDocumentPortalService portal) : ControllerBase
{
    [HttpGet("documents/{documentId:guid}/settings")]
    public async Task<IActionResult> GetDocumentSettings(Guid documentId, CancellationToken ct)
    {
        var profile = await portal.GetOrCreateProfileAsync(documentId, ct);
        var versions = await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new PublicVersionOptionDto(v.Id, v.VersionNumber, v.OriginalFileName, v.Extension, v.SizeBytes, false))
            .ToListAsync(ct);
        var latestId = versions.FirstOrDefault()?.Id;
        versions = versions.Select(v => v with { IsCurrent = v.Id == latestId }).ToList();

        return Ok(new DocumentPublicSettingsDto(
            profile.DocumentId,
            profile.Slug,
            profile.Summary,
            profile.PublicDescription,
            profile.DocumentType,
            profile.PublicCategoryId,
            profile.PublicCollectionId,
            profile.CoverImagePath,
            profile.PreviewAvailable,
            profile.DownloadAllowed,
            profile.PublicVisibilityStatus.ToString(),
            profile.PublishedAtUtc,
            profile.PublishStartAtUtc,
            profile.PublishEndAtUtc,
            profile.PublicVersionId,
            profile.Language,
            profile.Featured,
            profile.Pinned,
            profile.SeoTitle,
            profile.SeoDescription,
            profile.SeoKeywords,
            profile.PublicViewCount,
            profile.PublicDownloadCount,
            versions));
    }

    [HttpPatch("documents/{documentId:guid}/settings")]
    public async Task<IActionResult> UpdateDocumentSettings(Guid documentId, [FromBody] UpdateDocumentPublicSettingsRequest req, CancellationToken ct)
    {
        var profile = await portal.GetOrCreateProfileAsync(documentId, ct);
        if (req.Summary is not null) profile.Summary = req.Summary;
        if (req.PublicDescription is not null) profile.PublicDescription = req.PublicDescription;
        if (req.DocumentType is not null) profile.DocumentType = req.DocumentType;
        if (req.PublicCategoryId.HasValue) profile.PublicCategoryId = req.PublicCategoryId;
        if (req.PublicCollectionId.HasValue) profile.PublicCollectionId = req.PublicCollectionId;
        if (req.PreviewAvailable.HasValue) profile.PreviewAvailable = req.PreviewAvailable.Value;
        if (req.DownloadAllowed.HasValue) profile.DownloadAllowed = req.DownloadAllowed.Value;
        if (req.PublishStartAtUtc.HasValue) profile.PublishStartAtUtc = req.PublishStartAtUtc;
        if (req.PublishEndAtUtc.HasValue) profile.PublishEndAtUtc = req.PublishEndAtUtc;
        if (req.PublicVersionId.HasValue) profile.PublicVersionId = req.PublicVersionId;
        if (req.Language is not null) profile.Language = req.Language;
        if (req.Featured.HasValue) profile.Featured = req.Featured.Value;
        if (req.Pinned.HasValue) profile.Pinned = req.Pinned.Value;
        if (req.SeoTitle is not null) profile.SeoTitle = req.SeoTitle;
        if (req.SeoDescription is not null) profile.SeoDescription = req.SeoDescription;
        if (req.SeoKeywords is not null) profile.SeoKeywords = req.SeoKeywords;
        if (!string.IsNullOrWhiteSpace(req.PublicVisibilityStatus)
            && Enum.TryParse<PublicVisibilityStatus>(req.PublicVisibilityStatus, true, out var st))
            profile.PublicVisibilityStatus = st;

        profile.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "تنظیمات انتشار عمومی ذخیره شد" });
    }

    [HttpPost("documents/{documentId:guid}/publish")]
    public async Task<IActionResult> Publish(Guid documentId, CancellationToken ct)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });
        if (doc.IsArchived || doc.LifecycleStatus != DocumentLifecycleStatus.Active)
            return BadRequest(new { message = "سند بایگانی یا غیرفعال است" });

        var profile = await portal.GetOrCreateProfileAsync(documentId, ct);
        var now = DateTime.UtcNow;
        profile.PublicVisibilityStatus = PublicVisibilityStatus.Published;
        profile.PublishedAtUtc = now;
        profile.UpdatedAtUtc = now;
        if (profile.PublicVersionId is null)
        {
            var latest = await db.DocumentVersions.AsNoTracking()
                .Where(v => v.DocumentId == documentId)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => v.Id)
                .FirstOrDefaultAsync(ct);
            profile.PublicVersionId = latest == Guid.Empty ? null : latest;
        }
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "سند در پورتال عمومی منتشر شد", slug = profile.Slug });
    }

    [HttpPost("documents/{documentId:guid}/unpublish")]
    public async Task<IActionResult> Unpublish(Guid documentId, CancellationToken ct)
    {
        var profile = await db.DocumentPublicProfiles.FirstOrDefaultAsync(p => p.DocumentId == documentId, ct);
        if (profile is null) return NotFound(new { message = "پروفایل عمومی یافت نشد" });
        profile.PublicVisibilityStatus = PublicVisibilityStatus.Unpublished;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "انتشار عمومی لغو شد" });
    }

    [HttpGet("categories")]
    public async Task<IActionResult> ListCategories(CancellationToken ct) =>
        Ok(await db.PublicCategories.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(ct));

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] PublicCategoryUpsertRequest req, CancellationToken ct)
    {
        var slug = PublicPortalSlugHelper.ToSlug(req.Name);
        if (await db.PublicCategories.AnyAsync(c => c.Slug == slug, ct))
            slug = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
        var cat = new PublicCategory
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            Slug = slug,
            Description = req.Description,
            Icon = req.Icon,
            SortOrder = req.SortOrder,
            IsActive = req.IsActive,
        };
        db.PublicCategories.Add(cat);
        await db.SaveChangesAsync(ct);
        return Ok(cat);
    }

    [HttpGet("collections")]
    public async Task<IActionResult> ListCollections(CancellationToken ct) =>
        Ok(await db.PublicCollections.AsNoTracking().OrderBy(c => c.SortOrder).ToListAsync(ct));

    [HttpPost("collections")]
    public async Task<IActionResult> CreateCollection([FromBody] PublicCollectionUpsertRequest req, CancellationToken ct)
    {
        var slug = PublicPortalSlugHelper.ToSlug(req.Name);
        if (await db.PublicCollections.AnyAsync(c => c.Slug == slug, ct))
            slug = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
        var col = new PublicCollection
        {
            Id = Guid.NewGuid(),
            Name = req.Name.Trim(),
            Slug = slug,
            Description = req.Description,
            Featured = req.Featured,
            SortOrder = req.SortOrder,
            IsActive = req.IsActive,
        };
        db.PublicCollections.Add(col);
        await db.SaveChangesAsync(ct);
        return Ok(col);
    }

    [HttpGet("banners")]
    public async Task<IActionResult> ListBanners(CancellationToken ct) =>
        Ok(await db.PublicBanners.AsNoTracking().OrderBy(b => b.SortOrder).ToListAsync(ct));

    [HttpPost("banners")]
    public async Task<IActionResult> CreateBanner([FromBody] PublicBannerUpsertRequest req, CancellationToken ct)
    {
        var banner = new PublicBanner
        {
            Id = Guid.NewGuid(),
            Title = req.Title.Trim(),
            Subtitle = req.Subtitle,
            ImagePath = req.ImagePath,
            CtaLabel = req.CtaLabel,
            CtaUrl = req.CtaUrl,
            IsActive = req.IsActive,
            SortOrder = req.SortOrder,
        };
        db.PublicBanners.Add(banner);
        await db.SaveChangesAsync(ct);
        return Ok(banner);
    }

    [HttpGet("portal-settings")]
    public async Task<IActionResult> GetPortalSettings(CancellationToken ct) =>
        Ok(await portal.GetOrCreateSettingsAsync(ct));

    [HttpPut("portal-settings")]
    public async Task<IActionResult> UpdatePortalSettings([FromBody] PublicPortalSettingsUpdateRequest req, CancellationToken ct)
    {
        var s = await portal.GetOrCreateSettingsAsync(ct);
        var tracked = await db.PublicPortalSettings.FirstAsync(x => x.Id == s.Id, ct);
        if (req.PortalEnabled.HasValue) tracked.PortalEnabled = req.PortalEnabled.Value;
        if (req.SiteTitle is not null) tracked.SiteTitle = req.SiteTitle;
        if (req.LogoPath is not null) tracked.LogoPath = req.LogoPath;
        if (req.PrimaryColor is not null) tracked.PrimaryColor = req.PrimaryColor;
        if (req.SecondaryColor is not null) tracked.SecondaryColor = req.SecondaryColor;
        if (req.AboutText is not null) tracked.AboutText = req.AboutText;
        if (req.ShowViewCounts.HasValue) tracked.ShowViewCounts = req.ShowViewCounts.Value;
        if (req.ShowDownloadCounts.HasValue) tracked.ShowDownloadCounts = req.ShowDownloadCounts.Value;
        if (req.AllowDownloads.HasValue) tracked.AllowDownloads = req.AllowDownloads.Value;
        if (req.EnablePreviews.HasValue) tracked.EnablePreviews = req.EnablePreviews.Value;
        if (req.FeaturedSectionSize.HasValue) tracked.FeaturedSectionSize = req.FeaturedSectionSize.Value;
        tracked.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "تنظیمات پورتال ذخیره شد" });
    }
}

public record PublicCategoryUpsertRequest(string Name, string? Description, string? Icon, int SortOrder = 0, bool IsActive = true);
public record PublicCollectionUpsertRequest(string Name, string? Description, bool Featured = false, int SortOrder = 0, bool IsActive = true);
public record PublicBannerUpsertRequest(string Title, string? Subtitle, string? ImagePath, string? CtaLabel, string? CtaUrl, int SortOrder = 0, bool IsActive = true);
public record PublicPortalSettingsUpdateRequest(
    bool? PortalEnabled,
    string? SiteTitle,
    string? LogoPath,
    string? PrimaryColor,
    string? SecondaryColor,
    string? AboutText,
    bool? ShowViewCounts,
    bool? ShowDownloadCounts,
    bool? AllowDownloads,
    bool? EnablePreviews,
    int? FeaturedSectionSize);
