using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

/// <summary>
/// تشخیص ثبت قبلی — فقط برای همان لینک ارسال (DispatchLinkId).
/// لینک جدید یا پاسخ حذف‌شده اجازه ثبت مجدد می‌دهد.
/// </summary>
public static class FormDispatchDuplicateSubmissionChecker
{
    public static async Task<FormSubmission?> FindExistingAsync(
        AppDbContext db,
        FormDispatchLink link,
        CancellationToken ct = default) =>
        await db.FormSubmissions.AsNoTracking()
            .Where(s => s.DispatchLinkId == link.Id)
            .OrderByDescending(s => s.SubmittedAtUtc)
            .FirstOrDefaultAsync(ct);
}
