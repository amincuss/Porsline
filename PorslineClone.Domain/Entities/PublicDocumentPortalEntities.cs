namespace PorslineClone.Domain.Entities;

public enum PublicVisibilityStatus
{
    Draft = 0,
    ReviewPending = 1,
    ApprovedForPublic = 2,
    Published = 3,
    Unpublished = 4,
    Expired = 5,
}

/// <summary>تنظیمات انتشار عمومی سند — جداشده از Document داخلی.</summary>
public class DocumentPublicProfile
{
    public Guid DocumentId { get; set; }
    public Document Document { get; set; } = null!;
    public string Slug { get; set; } = "";
    public string? Summary { get; set; }
    public string? PublicDescription { get; set; }
    public string DocumentType { get; set; } = "Document";
    public Guid? PublicCategoryId { get; set; }
    public PublicCategory? PublicCategory { get; set; }
    public Guid? PublicCollectionId { get; set; }
    public PublicCollection? PublicCollection { get; set; }
    public string? CoverImagePath { get; set; }
    public bool PreviewAvailable { get; set; } = true;
    public bool DownloadAllowed { get; set; } = true;
    public PublicVisibilityStatus PublicVisibilityStatus { get; set; } = PublicVisibilityStatus.Draft;
    public DateTime? PublishedAtUtc { get; set; }
    public DateTime? PublishStartAtUtc { get; set; }
    public DateTime? PublishEndAtUtc { get; set; }
    public Guid? PublicVersionId { get; set; }
    public DocumentVersion? PublicVersion { get; set; }
    public string Language { get; set; } = "fa";
    public bool Featured { get; set; }
    public bool Pinned { get; set; }
    public string? SeoTitle { get; set; }
    public string? SeoDescription { get; set; }
    public string? SeoKeywords { get; set; }
    public long PublicViewCount { get; set; }
    public long PublicDownloadCount { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class PublicCategory
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string? CoverImagePath { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class PublicCollection
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string Slug { get; set; } = "";
    public string? Description { get; set; }
    public string? CoverImagePath { get; set; }
    public bool IsActive { get; set; } = true;
    public bool Featured { get; set; }
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class PublicBanner
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string? ImagePath { get; set; }
    public string? CtaLabel { get; set; }
    public string? CtaUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

public class PublicPortalSettings
{
    public Guid Id { get; set; }
    public bool PortalEnabled { get; set; } = true;
    public string SiteTitle { get; set; } = "گالری اسناد";
    public string? LogoPath { get; set; }
    public string PrimaryColor { get; set; } = "#4f46e5";
    public string SecondaryColor { get; set; } = "#0f172a";
    public string? AboutText { get; set; }
    public string? ContactEmail { get; set; }
    public string? ContactPhone { get; set; }
    public string? FooterLinksJson { get; set; }
    public string? SocialLinksJson { get; set; }
    public bool ShowViewCounts { get; set; } = true;
    public bool ShowDownloadCounts { get; set; } = true;
    public bool AllowDownloads { get; set; } = true;
    public bool EnablePreviews { get; set; } = true;
    public int FeaturedSectionSize { get; set; } = 6;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

public enum PublicAnalyticsEventType
{
    DocumentViewed = 1,
    DocumentDownloaded = 2,
    CollectionViewed = 3,
    CategoryViewed = 4,
}

public class PublicAnalyticsEvent
{
    public Guid Id { get; set; }
    public PublicAnalyticsEventType EventType { get; set; }
    public Guid? DocumentId { get; set; }
    public Guid? PublicCategoryId { get; set; }
    public Guid? PublicCollectionId { get; set; }
    public string? VisitorKey { get; set; }
    public string? IpHash { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
