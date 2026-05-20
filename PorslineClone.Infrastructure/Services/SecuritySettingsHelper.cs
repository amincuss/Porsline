using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public static class SecuritySettingsHelper
{
    public static async Task<SecuritySettings> GetAsync(AppDbContext db, CancellationToken ct = default)
    {
        var settings = await db.SecuritySettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return settings ?? new SecuritySettings();
    }

    public static int ClampLinkExpiryDays(int days) => Math.Clamp(days, 1, 365);

    public static int ClampAccessMinutes(int minutes) => Math.Clamp(minutes, 5, 1440);

    public static int ClampRefreshDays(int days) => Math.Clamp(days, 1, 90);

    public static DateTime LinkExpiresAtUtc(SecuritySettings settings) =>
        DateTime.UtcNow.AddDays(ClampLinkExpiryDays(settings.AnonymousLinkExpiryDays));

    public static bool DispatchLinkRequiresOtp(SecuritySettings settings, SmsSettings? sms = null) =>
        settings.DispatchLinkRequireOtp || (sms?.PublicFormRequireOtp ?? false);
}
