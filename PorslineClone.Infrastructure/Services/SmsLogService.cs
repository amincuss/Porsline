using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services;

public class SmsLogService(IServiceScopeFactory scopeFactory, ILogger<SmsLogService> logger) : ISmsLogService
{
    public async Task LogAsync(SmsLogEntry entry, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.SmsLogs.Add(MapNew(entry));
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to persist SMS log for {Mobile}", entry.MobileNumber);
        }
    }

    public async Task UpdateAsync(Guid logId, SmsLogEntry entry, CancellationToken ct = default)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.SmsLogs.FirstOrDefaultAsync(x => x.Id == logId, ct);
            if (row is null)
            {
                logger.LogWarning("SMS log {LogId} not found for update — skipped (no new row created)", logId);
                return;
            }

            ApplyEntry(row, entry);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to update SMS log {LogId}", logId);
        }
    }

    private static SmsLog MapNew(SmsLogEntry entry) => new()
    {
        Id = Guid.NewGuid(),
        MobileNumber = entry.MobileNumber,
        Message = entry.Message,
        IsSuccess = entry.IsSuccess,
        ErrorMessage = entry.ErrorMessage,
        TechnicalDetail = Truncate(entry.TechnicalDetail, 4000),
        Source = Truncate(entry.Source, 120),
        HttpStatusCode = entry.HttpStatusCode,
        CreatedAtUtc = DateTime.UtcNow,
    };

    private static void ApplyEntry(SmsLog row, SmsLogEntry entry)
    {
        row.MobileNumber = entry.MobileNumber;
        row.Message = entry.Message;
        row.IsSuccess = entry.IsSuccess;
        row.ErrorMessage = entry.ErrorMessage;
        row.TechnicalDetail = Truncate(entry.TechnicalDetail, 4000);
        row.HttpStatusCode = entry.HttpStatusCode;
        if (!string.IsNullOrWhiteSpace(entry.Source))
            row.Source = Truncate(entry.Source, 120);
        if (entry.IsSuccess)
            row.CreatedAtUtc = DateTime.UtcNow;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value[..max];
    }
}
