using Microsoft.EntityFrameworkCore;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public static class FormTrackingCodeGenerator
{
    /// <summary>کد پیگیری ۸ رقمی عددی برای ثبت فرم توسط پاسخگو.</summary>
    public static async Task<string> GenerateUniqueAsync(AppDbContext db, CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var code = Random.Shared.Next(10_000_000, 99_999_999).ToString();
            if (!await db.FormSubmissions.AnyAsync(x => x.TrackingCode == code, ct))
                return code;
        }

        var fallback = DateTime.UtcNow.ToString("yyMMdd") + Random.Shared.Next(1000, 9999).ToString();
        if (!await db.FormSubmissions.AnyAsync(x => x.TrackingCode == fallback, ct))
            return fallback;
        return Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    }
}
