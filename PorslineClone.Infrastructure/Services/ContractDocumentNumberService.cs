using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

/// <summary>تولید شماره یکتای سند قرارداد (DMS) با سریال ماهانه.</summary>
public static class ContractDocumentNumberService
{
    public static async Task<string> AllocateNextAsync(AppDbContext db, CancellationToken ct = default)
    {
        var period = int.Parse(DateTime.UtcNow.ToString("yyyyMM"));

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
                    DocumentNumberPrefix = "CNT",
                    DocumentSequencePeriod = period,
                    LastDocumentSequence = 0
                };
                db.ContractSettings.Add(settings);
            }

            if (settings.DocumentSequencePeriod != period)
            {
                settings.DocumentSequencePeriod = period;
                settings.LastDocumentSequence = 0;
            }

            settings.LastDocumentSequence++;
            var prefix = string.IsNullOrWhiteSpace(settings.DocumentNumberPrefix)
                ? "CNT"
                : settings.DocumentNumberPrefix.Trim().ToUpperInvariant();
            var number = $"{prefix}-{period}-{settings.LastDocumentSequence:D5}";

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
}
