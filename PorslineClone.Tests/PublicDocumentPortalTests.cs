using Xunit;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Tests;

public class PublicDocumentPortalTests
{
    [Fact]
    public void IsCurrentlyPublished_RequiresPublishedStatus()
    {
        var now = DateTime.UtcNow;
        var profile = new DocumentPublicProfile
        {
            PublicVisibilityStatus = PublicVisibilityStatus.Draft,
            PublishStartAtUtc = now.AddDays(-1),
            PublishEndAtUtc = now.AddDays(1),
        };
        Assert.False(PublicDocumentPortalService.IsCurrentlyPublished(profile, now));
    }

    [Fact]
    public void IsCurrentlyPublished_RespectsPublishWindow()
    {
        var now = DateTime.UtcNow;
        var profile = new DocumentPublicProfile
        {
            PublicVisibilityStatus = PublicVisibilityStatus.Published,
            PublishStartAtUtc = now.AddDays(1),
        };
        Assert.False(PublicDocumentPortalService.IsCurrentlyPublished(profile, now));

        profile.PublishStartAtUtc = now.AddDays(-1);
        profile.PublishEndAtUtc = now.AddDays(-1);
        Assert.False(PublicDocumentPortalService.IsCurrentlyPublished(profile, now));

        profile.PublishEndAtUtc = now.AddDays(1);
        Assert.True(PublicDocumentPortalService.IsCurrentlyPublished(profile, now));
    }

    [Fact]
    public void SlugHelper_NormalizesTitle()
    {
        var slug = PublicPortalSlugHelper.ToSlug("گزارش سالانه 1404");
        Assert.False(string.IsNullOrWhiteSpace(slug));
        Assert.DoesNotContain(" ", slug);
    }
}
