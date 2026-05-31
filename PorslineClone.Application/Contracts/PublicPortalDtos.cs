namespace PorslineClone.Application.Contracts;

public record PublicPortalBrandingDto(
    bool PortalEnabled,
    string SiteTitle,
    string? LogoUrl,
    string PrimaryColor,
    string SecondaryColor,
    string? AboutText,
    string? ContactEmail,
    string? ContactPhone,
    bool ShowViewCounts,
    bool ShowDownloadCounts,
    bool AllowDownloads,
    bool EnablePreviews);

public record PublicBannerDto(
    Guid Id,
    string Title,
    string? Subtitle,
    string? ImageUrl,
    string? CtaLabel,
    string? CtaUrl);

public record PublicCategoryListDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? CoverImageUrl,
    string? Icon,
    int DocumentCount);

public record PublicCollectionListDto(
    Guid Id,
    string Name,
    string Slug,
    string? Description,
    string? CoverImageUrl,
    bool Featured,
    int DocumentCount);

public record PublicDocumentCardDto(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string DocumentType,
    string? CategoryName,
    string? CategorySlug,
    string? CollectionName,
    string? CoverImageUrl,
    string FileType,
    long FileSizeBytes,
    bool PreviewAvailable,
    bool DownloadAllowed,
    DateTime? PublishedAtUtc,
    string Language,
    IReadOnlyList<string> Tags,
    long? ViewCount,
    long? DownloadCount,
    bool Featured,
    bool Pinned);

public record PublicDocumentDetailDto(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string? Description,
    string DocumentType,
    string? CategoryName,
    string? CategorySlug,
    string? CollectionName,
    string? CollectionSlug,
    string? CoverImageUrl,
    string FileType,
    string FileName,
    long FileSizeBytes,
    bool PreviewAvailable,
    bool DownloadAllowed,
    DateTime? PublishedAtUtc,
    string Language,
    IReadOnlyList<string> Tags,
    long? ViewCount,
    long? DownloadCount,
    bool Featured,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords,
    IReadOnlyList<PublicDocumentCardDto> RelatedDocuments);

public record PublicPagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount, int TotalPages);

public record PublicHomeDto(
    PublicPortalBrandingDto Branding,
    IReadOnlyList<PublicBannerDto> Banners,
    IReadOnlyList<PublicCategoryListDto> FeaturedCategories,
    IReadOnlyList<PublicCollectionListDto> FeaturedCollections,
    IReadOnlyList<PublicDocumentCardDto> LatestDocuments,
    IReadOnlyList<PublicDocumentCardDto> MostDownloaded,
    IReadOnlyList<PublicDocumentCardDto> HighlightedDocuments);

public record DocumentPublicSettingsDto(
    Guid DocumentId,
    string Slug,
    string? Summary,
    string? PublicDescription,
    string DocumentType,
    Guid? PublicCategoryId,
    Guid? PublicCollectionId,
    string? CoverImagePath,
    bool PreviewAvailable,
    bool DownloadAllowed,
    string PublicVisibilityStatus,
    DateTime? PublishedAtUtc,
    DateTime? PublishStartAtUtc,
    DateTime? PublishEndAtUtc,
    Guid? PublicVersionId,
    string Language,
    bool Featured,
    bool Pinned,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords,
    long PublicViewCount,
    long PublicDownloadCount,
    IReadOnlyList<PublicVersionOptionDto> VersionOptions);

public record PublicVersionOptionDto(Guid Id, int VersionNumber, string OriginalFileName, string Extension, long SizeBytes, bool IsCurrent);

public record UpdateDocumentPublicSettingsRequest(
    string? Summary,
    string? PublicDescription,
    string? DocumentType,
    Guid? PublicCategoryId,
    Guid? PublicCollectionId,
    bool? PreviewAvailable,
    bool? DownloadAllowed,
    string? PublicVisibilityStatus,
    DateTime? PublishStartAtUtc,
    DateTime? PublishEndAtUtc,
    Guid? PublicVersionId,
    string? Language,
    bool? Featured,
    bool? Pinned,
    string? SeoTitle,
    string? SeoDescription,
    string? SeoKeywords);
