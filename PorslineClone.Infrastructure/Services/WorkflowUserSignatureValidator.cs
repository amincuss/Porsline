using Microsoft.EntityFrameworkCore;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

/// <summary>تأییدکننده/اقدام‌کننده گردش باید امضای دیجیتال در پروفایل داشته باشد.</summary>
public static class WorkflowUserSignatureValidator
{
    public static async Task<string?> ValidateUserIdsAsync(
        AppDbContext db,
        IEnumerable<Guid> userIds,
        CancellationToken ct = default)
    {
        var ids = userIds.Where(x => x != Guid.Empty).Distinct().ToList();
        if (ids.Count == 0) return null;

        var rows = await db.Users.AsNoTracking()
            .Where(u => ids.Contains(u.Id) && !u.IsSoftDeleted)
            .Select(u => new
            {
                u.Id,
                Name = (u.FirstName + " " + u.LastName).Trim(),
                HasSignature = u.SignatureImagePath != null && u.SignatureImagePath != "",
            })
            .ToListAsync(ct);

        var missing = ids
            .Where(id => !rows.Any(r => r.Id == id && r.HasSignature))
            .ToList();
        if (missing.Count == 0) return null;

        var labels = missing
            .Select(id =>
            {
                var r = rows.FirstOrDefault(x => x.Id == id);
                return string.IsNullOrWhiteSpace(r?.Name) ? "کاربر انتخاب‌شده" : r!.Name;
            })
            .Distinct()
            .Take(3)
            .ToList();

        var names = string.Join("، ", labels);
        var more = missing.Count > labels.Count ? $" و {missing.Count - labels.Count} نفر دیگر" : "";
        return $"«{names}»{more} امضای دیجیتال ندارد. برای افزودن به گردش کار، ابتدا از پروفایل کاربر امضا آپلود شود.";
    }
}
