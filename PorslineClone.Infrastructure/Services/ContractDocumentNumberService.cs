using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

/// <summary>تولید شماره یکتای سند قرارداد؛ فرمت EN + سال شمسی + سریال ۴ رقمی (بدون خط تیره)، مثلاً EN14040001.</summary>
public static class ContractDocumentNumberService
{
    private const string NumberPrefix = "EN";

    public static async Task<string> AllocateNextAsync(AppDbContext db, CancellationToken ct = default)
    {
        var jalaliYear = GetCurrentJalaliYear();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var settings = await db.ContractSettings
                .Where(x => x.Id == 1)
                .FirstOrDefaultAsync(ct);

            if (settings is null)
            {
                settings = new ContractSettings
                {
                    Id = 1,
                    ApprovalEnabled = false,
                    DocumentNumberPrefix = NumberPrefix,
                    DocumentSequencePeriod = jalaliYear,
                    LastDocumentSequence = 0
                };
                db.ContractSettings.Add(settings);
            }

            if (settings.DocumentSequencePeriod != jalaliYear)
            {
                settings.DocumentSequencePeriod = jalaliYear;
                settings.LastDocumentSequence = 0;
            }

            settings.LastDocumentSequence++;
            settings.DocumentNumberPrefix = NumberPrefix;
            var number = $"{NumberPrefix}{jalaliYear}{settings.LastDocumentSequence:D4}";

            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return number;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    private static int GetCurrentJalaliYear()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("Iran Standard Time");
        var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        return new PersianCalendar().GetYear(local);
    }
}
