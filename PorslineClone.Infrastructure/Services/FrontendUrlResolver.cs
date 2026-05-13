using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PorslineClone.Application.Abstractions;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class FrontendUrlResolver(AppDbContext db, IConfiguration configuration) : IFrontendUrlResolver
{
    public async Task<string?> ResolvePublicBaseUrlAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var fromDb = TrimUrl(row?.PublicBaseUrl);
        if (!string.IsNullOrWhiteSpace(fromDb)) return fromDb;
        return TrimUrl(configuration["Frontend:BaseUrl"]);
    }

    public async Task<string?> ResolveAdminBaseUrlAsync(CancellationToken cancellationToken = default)
    {
        var row = await db.SiteSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        var fromDb = TrimUrl(row?.AdminPanelBaseUrl);
        if (!string.IsNullOrWhiteSpace(fromDb)) return fromDb;
        return await ResolvePublicBaseUrlAsync(cancellationToken);
    }

    private static string? TrimUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim().TrimEnd('/');
    }
}
