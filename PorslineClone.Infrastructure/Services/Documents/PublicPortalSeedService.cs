using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Documents;

public static class PublicPortalSeedService
{
    public static async Task SeedDemoContentAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (!await db.Database.CanConnectAsync(ct)) return;

        if (!await db.PublicPortalSettings.AnyAsync(ct))
        {
            db.PublicPortalSettings.Add(new PublicPortalSettings
            {
                Id = PublicDocumentPortalService.PortalSettingsId,
                SiteTitle = "گالری اسناد سازمان",
                AboutText = "مرجع رسمی اسناد، بروشورها و گزارش‌های منتشرشده برای عموم.",
                ContactEmail = "info@example.com",
                PrimaryColor = "#4f46e5",
            });
        }

        PublicCategory? cat = null;
        if (!await db.PublicCategories.AnyAsync(ct))
        {
            cat = new PublicCategory
            {
                Id = Guid.Parse("c1000000-0000-0000-0000-000000000001"),
                Name = "گزارش‌ها و بروشورها",
                Slug = "reports-brochures",
                Description = "گزارش‌های عمومی و بروشورهای سازمانی",
                Icon = "FileText",
                SortOrder = 0,
            };
            db.PublicCategories.Add(cat);
        }
        else
        {
            cat = await db.PublicCategories.OrderBy(c => c.SortOrder).FirstAsync(ct);
        }

        if (!await db.PublicCollections.AnyAsync(ct))
        {
            db.PublicCollections.Add(new PublicCollection
            {
                Id = Guid.Parse("c2000000-0000-0000-0000-000000000001"),
                Name = "اسناد ویژه",
                Slug = "featured-docs",
                Description = "مجموعه منتخب اسناد پرکاربرد",
                Featured = true,
                SortOrder = 0,
            });
        }

        if (!await db.PublicBanners.AnyAsync(ct))
        {
            db.PublicBanners.Add(new PublicBanner
            {
                Id = Guid.Parse("c3000000-0000-0000-0000-000000000001"),
                Title = "به گالری اسناد خوش آمدید",
                Subtitle = "جستجو، پیش‌نمایش و دانلود امن اسناد منتشرشده",
                CtaLabel = "شروع جستجو",
                CtaUrl = "/portal/search",
                SortOrder = 0,
            });
        }

        await db.SaveChangesAsync(ct);

        var docs = await db.Documents
            .Where(d => !d.IsDeleted && !d.IsArchived && d.LifecycleStatus == DocumentLifecycleStatus.Active)
            .OrderByDescending(d => d.CreatedAtUtc)
            .Take(3)
            .ToListAsync(ct);

        foreach (var doc in docs)
        {
            if (await db.DocumentPublicProfiles.AnyAsync(p => p.DocumentId == doc.Id, ct)) continue;

            var slug = await PublicPortalSlugHelper.EnsureUniqueDocumentSlugAsync(
                s => db.DocumentPublicProfiles.AnyAsync(p => p.Slug == s, ct),
                doc.Title,
                doc.Id,
                ct);

            var versionId = await db.DocumentVersions
                .Where(v => v.DocumentId == doc.Id)
                .OrderByDescending(v => v.VersionNumber)
                .Select(v => v.Id)
                .FirstOrDefaultAsync(ct);

            db.DocumentPublicProfiles.Add(new DocumentPublicProfile
            {
                DocumentId = doc.Id,
                Slug = slug,
                Summary = doc.Description?.Length > 200 ? doc.Description[..200] : doc.Description,
                PublicCategoryId = cat?.Id,
                PublicVisibilityStatus = PublicVisibilityStatus.Published,
                PublishedAtUtc = DateTime.UtcNow,
                PublicVersionId = versionId == Guid.Empty ? null : versionId,
                Featured = doc == docs.First(),
                DownloadAllowed = true,
                PreviewAvailable = true,
            });
        }

        await db.SaveChangesAsync(ct);
    }
}
