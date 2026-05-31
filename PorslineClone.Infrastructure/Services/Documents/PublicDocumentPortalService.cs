using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Documents;

public sealed class PublicDocumentPortalService(AppDbContext db)
{
    public static readonly Guid PortalSettingsId = Guid.Parse("b2c3d4e5-f6a7-8901-bcde-f123456789ab");

    public static bool IsCurrentlyPublished(DocumentPublicProfile p, DateTime utcNow) =>
        p.PublicVisibilityStatus == PublicVisibilityStatus.Published
        && (p.PublishStartAtUtc is null || p.PublishStartAtUtc <= utcNow)
        && (p.PublishEndAtUtc is null || p.PublishEndAtUtc > utcNow);

    public IQueryable<DocumentPublicProfile> PublishedProfilesQuery(DateTime utcNow) =>
        db.DocumentPublicProfiles.AsNoTracking()
            .Where(p => p.PublicVisibilityStatus == PublicVisibilityStatus.Published)
            .Where(p => p.PublishStartAtUtc == null || p.PublishStartAtUtc <= utcNow)
            .Where(p => p.PublishEndAtUtc == null || p.PublishEndAtUtc > utcNow)
            .Join(
                db.Documents.AsNoTracking().Where(d => !d.IsDeleted && !d.IsArchived && d.LifecycleStatus == DocumentLifecycleStatus.Active),
                p => p.DocumentId,
                d => d.Id,
                (p, d) => p);

    public async Task<PublicPortalSettings> GetOrCreateSettingsAsync(CancellationToken ct = default)
    {
        var s = await db.PublicPortalSettings.AsNoTracking().FirstOrDefaultAsync(x => x.Id == PortalSettingsId, ct);
        if (s is not null) return s;
        s = new PublicPortalSettings { Id = PortalSettingsId, SiteTitle = "گالری اسناد" };
        db.PublicPortalSettings.Add(s);
        await db.SaveChangesAsync(ct);
        return s;
    }

    public async Task<PublicPortalBrandingDto> GetBrandingAsync(CancellationToken ct = default)
    {
        var s = await GetOrCreateSettingsAsync(ct);
        return new PublicPortalBrandingDto(
            s.PortalEnabled,
            s.SiteTitle,
            ToPublicUrl(s.LogoPath),
            s.PrimaryColor,
            s.SecondaryColor,
            s.AboutText,
            s.ContactEmail,
            s.ContactPhone,
            s.ShowViewCounts,
            s.ShowDownloadCounts,
            s.AllowDownloads,
            s.EnablePreviews);
    }

    public async Task<PublicHomeDto> GetHomeAsync(CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        if (!settings.PortalEnabled)
            throw new InvalidOperationException("پورتال عمومی غیرفعال است");

        var now = DateTime.UtcNow;
        var featuredSize = Math.Clamp(settings.FeaturedSectionSize, 3, 24);
        var published = PublishedProfilesQuery(now);

        var banners = await db.PublicBanners.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Select(x => new PublicBannerDto(x.Id, x.Title, x.Subtitle, ToPublicUrl(x.ImagePath), x.CtaLabel, x.CtaUrl))
            .Take(5)
            .ToListAsync(ct);

        var categories = await db.PublicCategories.AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.SortOrder)
            .Take(8)
            .ToListAsync(ct);

        var categoryDtos = new List<PublicCategoryListDto>();
        foreach (var c in categories)
        {
            var count = await published.CountAsync(p => p.PublicCategoryId == c.Id, ct);
            categoryDtos.Add(new PublicCategoryListDto(c.Id, c.Name, c.Slug, c.Description, ToPublicUrl(c.CoverImagePath), c.Icon, count));
        }

        var collections = await db.PublicCollections.AsNoTracking()
            .Where(x => x.IsActive && x.Featured)
            .OrderBy(x => x.SortOrder)
            .Take(6)
            .ToListAsync(ct);

        var collectionDtos = new List<PublicCollectionListDto>();
        foreach (var col in collections)
        {
            var count = await published.CountAsync(p => p.PublicCollectionId == col.Id, ct);
            collectionDtos.Add(new PublicCollectionListDto(col.Id, col.Name, col.Slug, col.Description, ToPublicUrl(col.CoverImagePath), col.Featured, count));
        }

        var latest = await MapCardsAsync(
            published.OrderByDescending(p => p.PublishedAtUtc ?? p.UpdatedAtUtc).Take(featuredSize),
            settings,
            ct);

        var mostDownloaded = await MapCardsAsync(
            published.OrderByDescending(p => p.PublicDownloadCount).ThenByDescending(p => p.PublishedAtUtc).Take(featuredSize),
            settings,
            ct);

        var highlighted = await MapCardsAsync(
            published.Where(p => p.Featured || p.Pinned)
                .OrderByDescending(p => p.Pinned)
                .ThenByDescending(p => p.Featured)
                .ThenByDescending(p => p.PublishedAtUtc)
                .Take(featuredSize),
            settings,
            ct);

        return new PublicHomeDto(
            await GetBrandingAsync(ct),
            banners,
            categoryDtos,
            collectionDtos,
            latest,
            mostDownloaded,
            highlighted);
    }

    public async Task<PublicPagedResult<PublicDocumentCardDto>> ListDocumentsAsync(
        PublicDocumentListQuery q,
        CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        var now = DateTime.UtcNow;
        var query = ApplyFilters(PublishedProfilesQuery(now), q);

        var total = await query.CountAsync(ct);
        query = ApplySort(query, q.Sort, db);
        var page = Math.Max(1, q.Page);
        var pageSize = Math.Clamp(q.PageSize, 1, 48);
        var items = await MapCardsAsync(
            query.Skip((page - 1) * pageSize).Take(pageSize),
            settings,
            ct);

        return new PublicPagedResult<PublicDocumentCardDto>(
            items,
            page,
            pageSize,
            total,
            (int)Math.Ceiling(total / (double)pageSize));
    }

    public async Task<PublicDocumentDetailDto?> GetDocumentBySlugAsync(string slug, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        var now = DateTime.UtcNow;
        var profile = await PublishedProfilesQuery(now).FirstOrDefaultAsync(p => p.Slug == slug, ct);
        if (profile is null) return null;

        var doc = await db.Documents.AsNoTracking()
            .Include(d => d.Tags)
            .FirstOrDefaultAsync(d => d.Id == profile.DocumentId, ct);
        if (doc is null) return null;

        var version = await ResolvePublicVersionAsync(profile, ct);
        if (version is null) return null;

        var category = profile.PublicCategoryId.HasValue
            ? await db.PublicCategories.AsNoTracking().FirstOrDefaultAsync(c => c.Id == profile.PublicCategoryId, ct)
            : null;
        var collection = profile.PublicCollectionId.HasValue
            ? await db.PublicCollections.AsNoTracking().FirstOrDefaultAsync(c => c.Id == profile.PublicCollectionId, ct)
            : null;

        var related = await MapCardsAsync(
            PublishedProfilesQuery(now)
                .Where(p => p.DocumentId != profile.DocumentId)
                .Where(p => p.PublicCategoryId == profile.PublicCategoryId || p.PublicCollectionId == profile.PublicCollectionId)
                .OrderByDescending(p => p.Featured)
                .Take(6),
            settings,
            ct);

        return new PublicDocumentDetailDto(
            doc.Id,
            profile.Slug,
            doc.Title,
            profile.Summary,
            profile.PublicDescription ?? doc.Description,
            profile.DocumentType,
            category?.Name,
            category?.Slug,
            collection?.Name,
            collection?.Slug,
            ToPublicUrl(profile.CoverImagePath),
            NormalizeFileType(version.Extension),
            version.OriginalFileName,
            version.SizeBytes,
            profile.PreviewAvailable && settings.EnablePreviews,
            profile.DownloadAllowed && settings.AllowDownloads,
            profile.PublishedAtUtc,
            profile.Language,
            doc.Tags.Select(t => t.Tag).ToList(),
            settings.ShowViewCounts ? profile.PublicViewCount : null,
            settings.ShowDownloadCounts ? profile.PublicDownloadCount : null,
            profile.Featured,
            profile.SeoTitle ?? doc.Title,
            profile.SeoDescription ?? profile.Summary,
            profile.SeoKeywords,
            related);
    }

    public async Task<DocumentVersion?> ResolvePublicVersionAsync(DocumentPublicProfile profile, CancellationToken ct)
    {
        if (profile.PublicVersionId.HasValue)
        {
            return await db.DocumentVersions.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == profile.PublicVersionId && v.DocumentId == profile.DocumentId, ct);
        }

        return await db.DocumentVersions.AsNoTracking()
            .Where(v => v.DocumentId == profile.DocumentId)
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<bool> RecordViewAsync(Guid documentId, string? visitorKey, string? ip, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var profile = await PublishedProfilesQuery(now).FirstOrDefaultAsync(p => p.DocumentId == documentId, ct);
        if (profile is null) return false;

        var key = BuildVisitorKey(visitorKey, ip);
        var hourAgo = now.AddHours(-1);
        var recent = await db.PublicAnalyticsEvents.AsNoTracking()
            .AnyAsync(e =>
                e.DocumentId == documentId
                && e.EventType == PublicAnalyticsEventType.DocumentViewed
                && e.VisitorKey == key
                && e.CreatedAtUtc >= hourAgo, ct);
        if (recent) return true;

        var tracked = await db.DocumentPublicProfiles.FirstOrDefaultAsync(p => p.DocumentId == documentId, ct);
        if (tracked is null) return false;
        tracked.PublicViewCount++;
        tracked.UpdatedAtUtc = now;
        db.PublicAnalyticsEvents.Add(new PublicAnalyticsEvent
        {
            Id = Guid.NewGuid(),
            EventType = PublicAnalyticsEventType.DocumentViewed,
            DocumentId = documentId,
            VisitorKey = key,
            IpHash = HashIp(ip),
            CreatedAtUtc = now,
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> RecordDownloadAsync(Guid documentId, string? visitorKey, string? ip, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        if (!settings.AllowDownloads) return false;

        var now = DateTime.UtcNow;
        var profile = await db.DocumentPublicProfiles
            .FirstOrDefaultAsync(p => p.DocumentId == documentId, ct);
        if (profile is null || !IsCurrentlyPublished(profile, now) || !profile.DownloadAllowed) return false;

        var key = BuildVisitorKey(visitorKey, ip);
        var recent = await db.PublicAnalyticsEvents.AsNoTracking()
            .AnyAsync(e =>
                e.DocumentId == documentId
                && e.EventType == PublicAnalyticsEventType.DocumentDownloaded
                && e.VisitorKey == key
                && e.CreatedAtUtc >= now.AddMinutes(-5), ct);
        if (!recent)
        {
            profile.PublicDownloadCount++;
            profile.UpdatedAtUtc = now;
            db.PublicAnalyticsEvents.Add(new PublicAnalyticsEvent
            {
                Id = Guid.NewGuid(),
                EventType = PublicAnalyticsEventType.DocumentDownloaded,
                DocumentId = documentId,
                VisitorKey = key,
                IpHash = HashIp(ip),
                CreatedAtUtc = now,
            });
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    public async Task<DocumentPublicProfile> GetOrCreateProfileAsync(Guid documentId, CancellationToken ct = default)
    {
        var profile = await db.DocumentPublicProfiles.FirstOrDefaultAsync(p => p.DocumentId == documentId, ct);
        if (profile is not null) return profile;

        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.Id == documentId && !d.IsDeleted, ct)
            ?? throw new InvalidOperationException("سند یافت نشد");

        var slug = await PublicPortalSlugHelper.EnsureUniqueDocumentSlugAsync(
            s => db.DocumentPublicProfiles.AnyAsync(p => p.Slug == s, ct),
            doc.Title,
            documentId,
            ct);

        profile = new DocumentPublicProfile
        {
            DocumentId = documentId,
            Slug = slug,
            Summary = doc.Description?.Length > 500 ? doc.Description[..500] : doc.Description,
            DocumentType = "Document",
            PublicVisibilityStatus = PublicVisibilityStatus.Draft,
        };
        db.DocumentPublicProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return profile;
    }

    private IQueryable<DocumentPublicProfile> ApplyFilters(IQueryable<DocumentPublicProfile> query, PublicDocumentListQuery q)
    {
        if (!string.IsNullOrWhiteSpace(q.Q))
        {
            var term = q.Q.Trim();
            query = query.Where(p =>
                db.Documents.Any(d => d.Id == p.DocumentId && (d.Title.Contains(term) || (p.Summary != null && p.Summary.Contains(term)) || (p.PublicDescription != null && p.PublicDescription.Contains(term))))
                || db.DocumentTags.Any(t => t.DocumentId == p.DocumentId && t.Tag.Contains(term)));
        }
        if (!string.IsNullOrWhiteSpace(q.CategorySlug))
        {
            var slug = q.CategorySlug.Trim();
            query = query.Where(p => p.PublicCategoryId != null
                && db.PublicCategories.Any(c => c.Id == p.PublicCategoryId && c.Slug == slug && c.IsActive));
        }
        if (!string.IsNullOrWhiteSpace(q.CollectionSlug))
        {
            var slug = q.CollectionSlug.Trim();
            query = query.Where(p => p.PublicCollectionId != null
                && db.PublicCollections.Any(c => c.Id == p.PublicCollectionId && c.Slug == slug && c.IsActive));
        }
        if (q.CategoryId.HasValue)
            query = query.Where(p => p.PublicCategoryId == q.CategoryId);
        if (q.CollectionId.HasValue)
            query = query.Where(p => p.PublicCollectionId == q.CollectionId);
        if (!string.IsNullOrWhiteSpace(q.FileType))
            query = query.Where(p => db.DocumentVersions.Any(v =>
                v.DocumentId == p.DocumentId
                && (p.PublicVersionId == null ? v.VersionNumber == db.DocumentVersions.Where(x => x.DocumentId == p.DocumentId).Max(x => x.VersionNumber) : v.Id == p.PublicVersionId)
                && v.Extension == q.FileType));
        if (!string.IsNullOrWhiteSpace(q.Language))
            query = query.Where(p => p.Language == q.Language);
        if (q.FeaturedOnly == true)
            query = query.Where(p => p.Featured);
        if (q.DownloadableOnly == true)
            query = query.Where(p => p.DownloadAllowed);
        if (q.Tag is not null)
            query = query.Where(p => db.DocumentTags.Any(t => t.DocumentId == p.DocumentId && t.Tag == q.Tag));
        if (q.PublishedFrom.HasValue)
            query = query.Where(p => p.PublishedAtUtc >= q.PublishedFrom);
        if (q.PublishedTo.HasValue)
            query = query.Where(p => p.PublishedAtUtc <= q.PublishedTo);
        return query;
    }

    private static IQueryable<DocumentPublicProfile> ApplySort(IQueryable<DocumentPublicProfile> query, string? sort, AppDbContext db) =>
        (sort ?? "newest").ToLowerInvariant() switch
        {
            "oldest" => query.OrderBy(p => p.PublishedAtUtc),
            "title_asc" => query.OrderBy(p => db.Documents.Where(d => d.Id == p.DocumentId).Select(d => d.Title).FirstOrDefault()),
            "title_desc" => query.OrderByDescending(p => db.Documents.Where(d => d.Id == p.DocumentId).Select(d => d.Title).FirstOrDefault()),
            "most_viewed" => query.OrderByDescending(p => p.PublicViewCount),
            "most_downloaded" => query.OrderByDescending(p => p.PublicDownloadCount),
            _ => query.OrderByDescending(p => p.Pinned).ThenByDescending(p => p.PublishedAtUtc ?? p.UpdatedAtUtc),
        };

    private async Task<IReadOnlyList<PublicDocumentCardDto>> MapCardsAsync(
        IQueryable<DocumentPublicProfile> query,
        PublicPortalSettings settings,
        CancellationToken ct)
    {
        var profiles = await query.ToListAsync(ct);
        if (profiles.Count == 0) return [];

        var docIds = profiles.Select(p => p.DocumentId).ToList();
        var docs = await db.Documents.AsNoTracking()
            .Where(d => docIds.Contains(d.Id))
            .Select(d => new { d.Id, d.Title })
            .ToDictionaryAsync(d => d.Id, ct);

        var catIds = profiles.Where(p => p.PublicCategoryId.HasValue).Select(p => p.PublicCategoryId!.Value).Distinct().ToList();
        var cats = await db.PublicCategories.AsNoTracking()
            .Where(c => catIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var colIds = profiles.Where(p => p.PublicCollectionId.HasValue).Select(p => p.PublicCollectionId!.Value).Distinct().ToList();
        var cols = await db.PublicCollections.AsNoTracking()
            .Where(c => colIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var tags = await db.DocumentTags.AsNoTracking()
            .Where(t => docIds.Contains(t.DocumentId))
            .GroupBy(t => t.DocumentId)
            .ToDictionaryAsync(g => g.Key, g => g.Select(x => x.Tag).ToList(), ct);

        var versions = new Dictionary<Guid, DocumentVersion>();
        foreach (var p in profiles)
        {
            var v = await ResolvePublicVersionAsync(p, ct);
            if (v is not null) versions[p.DocumentId] = v;
        }

        return profiles.Select(p =>
        {
            docs.TryGetValue(p.DocumentId, out var doc);
            PublicCategory? cat = null;
            if (p.PublicCategoryId.HasValue) cats.TryGetValue(p.PublicCategoryId.Value, out cat);
            PublicCollection? col = null;
            if (p.PublicCollectionId.HasValue) cols.TryGetValue(p.PublicCollectionId.Value, out col);
            versions.TryGetValue(p.DocumentId, out var ver);
            tags.TryGetValue(p.DocumentId, out var docTags);
            return new PublicDocumentCardDto(
                p.DocumentId,
                p.Slug,
                doc?.Title ?? "",
                p.Summary,
                p.DocumentType,
                cat?.Name,
                cat?.Slug,
                col?.Name,
                ToPublicUrl(p.CoverImagePath),
                ver is not null ? NormalizeFileType(ver.Extension) : "file",
                ver?.SizeBytes ?? 0,
                p.PreviewAvailable && settings.EnablePreviews,
                p.DownloadAllowed && settings.AllowDownloads,
                p.PublishedAtUtc,
                p.Language,
                docTags ?? [],
                settings.ShowViewCounts ? p.PublicViewCount : null,
                settings.ShowDownloadCounts ? p.PublicDownloadCount : null,
                p.Featured,
                p.Pinned);
        }).ToList();
    }

    private static string NormalizeFileType(string ext)
    {
        var e = ext.Trim().TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(e) ? "file" : e;
    }

    private static string? ToPublicUrl(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return path;
        return path.StartsWith('/') ? path : $"/{path}";
    }

    private static string BuildVisitorKey(string? visitorKey, string? ip) =>
        !string.IsNullOrWhiteSpace(visitorKey) ? visitorKey.Trim()[..Math.Min(64, visitorKey.Trim().Length)] : HashIp(ip) ?? Guid.NewGuid().ToString("N");

    private static string? HashIp(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ip.Trim()));
        return Convert.ToHexString(bytes)[..16];
    }
}

public sealed class PublicDocumentListQuery
{
    public string? Q { get; set; }
    public string? CategorySlug { get; set; }
    public string? CollectionSlug { get; set; }
    public Guid? CategoryId { get; set; }
    public Guid? CollectionId { get; set; }
    public string? FileType { get; set; }
    public string? Language { get; set; }
    public string? Tag { get; set; }
    public bool? FeaturedOnly { get; set; }
    public bool? DownloadableOnly { get; set; }
    public DateTime? PublishedFrom { get; set; }
    public DateTime? PublishedTo { get; set; }
    public string? Sort { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 12;
}
