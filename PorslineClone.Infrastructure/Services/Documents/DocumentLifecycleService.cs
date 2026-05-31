using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.Documents;

public sealed class DocumentLifecycleService(
    AppDbContext db,
    IInboxMessageService inbox)
{
    private static readonly Guid SettingsSingletonId = Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");

    public async Task<DocumentLifecycleSettings> GetOrCreateSettingsAsync(CancellationToken ct = default)
    {
        var settings = await db.DocumentLifecycleSettings.FirstOrDefaultAsync(x => x.Id == SettingsSingletonId, ct);
        if (settings is not null) return settings;

        settings = new DocumentLifecycleSettings
        {
            Id = SettingsSingletonId,
            AutoProcessEnabled = true,
            DefaultExpirationWarningDays = 30,
            ProcessIntervalHours = 6,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        db.DocumentLifecycleSettings.Add(settings);
        await db.SaveChangesAsync(ct);
        return settings;
    }

    public async Task ApplyMatchingPolicyAsync(Document doc, CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        DocumentRetentionPolicy? policy = null;

        if (settings.DefaultRetentionPolicyId.HasValue)
        {
            policy = await db.DocumentRetentionPolicies.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == settings.DefaultRetentionPolicyId && x.IsActive, ct);
        }

        if (policy is null)
        {
            policy = await db.DocumentRetentionPolicies.AsNoTracking()
                .Where(x => x.IsActive && x.CategoryMatch != null && x.CategoryMatch == doc.Category)
                .OrderBy(x => x.SortOrder)
                .FirstOrDefaultAsync(ct);
        }

        if (policy is null)
        {
            policy = await db.DocumentRetentionPolicies.AsNoTracking()
                .Where(x => x.IsActive && x.IsDefault)
                .OrderBy(x => x.SortOrder)
                .FirstOrDefaultAsync(ct);
        }

        if (policy is null) return;

        doc.RetentionPolicyId = policy.Id;
        if (policy.LongTermRetention)
            doc.LongTermRetention = true;
        RecalculateSchedule(doc, policy, settings.DefaultExpirationWarningDays);
    }

    public void RecalculateSchedule(Document doc, DocumentRetentionPolicy? policy, int defaultWarningDays)
    {
        var baseDate = doc.DocumentDateUtc ?? doc.CreatedAtUtc;

        if (policy?.LongTermRetention == true || doc.LongTermRetention)
        {
            doc.LongTermRetention = true;
            doc.ScheduledDeleteAtUtc = null;
        }

        if (policy?.ArchiveAfterDays is int archiveDays && archiveDays > 0 && !doc.IsArchived)
            doc.ScheduledArchiveAtUtc = baseDate.AddDays(archiveDays);
        else if (doc.IsArchived)
            doc.ScheduledArchiveAtUtc = null;

        if (!doc.LongTermRetention)
        {
            DateTime? deleteAt = null;
            if (policy?.DeleteAfterDays is int deleteDays && deleteDays > 0)
                deleteAt = baseDate.AddDays(deleteDays);
            if (doc.ExpiresAtUtc.HasValue && (deleteAt is null || doc.ExpiresAtUtc < deleteAt))
                deleteAt = doc.ExpiresAtUtc;
            doc.ScheduledDeleteAtUtc = deleteAt;
        }

        _ = defaultWarningDays;
        doc.LifecycleWarningSentAtUtc = null;
    }

    public void ArchiveDocument(Document doc, DocumentArchiveTier tier, Guid? actorUserId = null)
    {
        var now = DateTime.UtcNow;
        doc.IsArchived = true;
        doc.ArchivedAtUtc = now;
        doc.ArchiveTier = tier;
        doc.LifecycleStatus = DocumentLifecycleStatus.Archived;
        doc.ScheduledArchiveAtUtc = null;
        doc.UpdatedAtUtc = now;

        db.DocumentActivities.Add(new DocumentActivity
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            EventType = "Archive",
            Message = tier == DocumentArchiveTier.Cold ? "سند به بایگانی سرد منتقل شد" : "سند بایگانی شد (گرم)",
            ActorUserId = actorUserId,
            CreatedAtUtc = now,
        });
    }

    public void RestoreFromArchive(Document doc, Guid? actorUserId = null)
    {
        var now = DateTime.UtcNow;
        doc.IsArchived = false;
        doc.ArchivedAtUtc = null;
        doc.ArchiveTier = DocumentArchiveTier.None;
        doc.LifecycleStatus = doc.IsObsolete ? DocumentLifecycleStatus.Obsolete : DocumentLifecycleStatus.Active;
        doc.UpdatedAtUtc = now;

        db.DocumentActivities.Add(new DocumentActivity
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            EventType = "Restore",
            Message = "سند از بایگانی بازیابی شد",
            ActorUserId = actorUserId,
            CreatedAtUtc = now,
        });
    }

    public void SetLegalHold(Document doc, bool hold, string? reason, Guid userId)
    {
        var now = DateTime.UtcNow;
        doc.LegalHold = hold;
        doc.LegalHoldReason = hold ? (reason ?? "").Trim() : null;
        doc.LegalHoldStartedAtUtc = hold ? now : null;
        doc.LegalHoldByUserId = hold ? userId : null;
        doc.UpdatedAtUtc = now;

        db.DocumentActivities.Add(new DocumentActivity
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            EventType = hold ? "LegalHold" : "LegalHoldRelease",
            Message = hold ? "Legal Hold فعال شد — حذف خودکار متوقف شد" : "Legal Hold برداشته شد",
            Reason = hold ? doc.LegalHoldReason : null,
            ActorUserId = userId,
            CreatedAtUtc = now,
        });
    }

    public void MarkObsolete(Document doc, string? reason, Guid? actorUserId = null)
    {
        var now = DateTime.UtcNow;
        doc.IsObsolete = true;
        doc.ObsoleteAtUtc = now;
        doc.ObsoleteReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        doc.LifecycleStatus = DocumentLifecycleStatus.Obsolete;
        doc.UpdatedAtUtc = now;

        db.DocumentActivities.Add(new DocumentActivity
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            EventType = "Obsolete",
            Message = "سند منسوخ علامت‌گذاری شد",
            Reason = doc.ObsoleteReason,
            ActorUserId = actorUserId,
            CreatedAtUtc = now,
        });
    }

    public void ClearObsolete(Document doc, Guid? actorUserId = null)
    {
        var now = DateTime.UtcNow;
        doc.IsObsolete = false;
        doc.ObsoleteAtUtc = null;
        doc.ObsoleteReason = null;
        doc.LifecycleStatus = doc.IsArchived ? DocumentLifecycleStatus.Archived : DocumentLifecycleStatus.Active;
        doc.UpdatedAtUtc = now;

        db.DocumentActivities.Add(new DocumentActivity
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            EventType = "Restore",
            Message = "وضعیت منسوخ برداشته شد",
            ActorUserId = actorUserId,
            CreatedAtUtc = now,
        });
    }

    public async Task<int> ProcessDueDocumentsAsync(CancellationToken ct = default)
    {
        var settings = await GetOrCreateSettingsAsync(ct);
        if (!settings.AutoProcessEnabled) return 0;

        var now = DateTime.UtcNow;
        var warningThreshold = now.AddDays(Math.Max(1, settings.DefaultExpirationWarningDays));
        var actions = 0;

        var expiring = await db.Documents
            .Where(d => !d.LegalHold && d.LifecycleWarningSentAtUtc == null)
            .Where(d =>
                (d.ExpiresAtUtc != null && d.ExpiresAtUtc <= warningThreshold)
                || (d.ScheduledDeleteAtUtc != null && d.ScheduledDeleteAtUtc <= warningThreshold))
            .ToListAsync(ct);

        foreach (var doc in expiring)
        {
            var targetDate = doc.ExpiresAtUtc ?? doc.ScheduledDeleteAtUtc;
            var title = doc.ExpiresAtUtc.HasValue ? "هشدار انقضای سند" : "هشدار حذف خودکار سند";
            var body = $"سند «{doc.Title}» تا {targetDate:yyyy/MM/dd} منقضی/حذف می‌شود. Legal Hold مانع حذف می‌شود.";
            await inbox.SendToUserAsync(doc.OwnerUserId, title, body, ct);
            doc.LifecycleWarningSentAtUtc = now;
            doc.UpdatedAtUtc = now;
            actions++;
        }

        var toArchive = await db.Documents
            .Where(d => !d.IsArchived && !d.LegalHold && !d.IsObsolete)
            .Where(d => d.ScheduledArchiveAtUtc != null && d.ScheduledArchiveAtUtc <= now)
            .ToListAsync(ct);

        foreach (var doc in toArchive)
        {
            ArchiveDocument(doc, DocumentArchiveTier.Warm);
            actions++;
        }

        var warmArchived = await db.Documents
            .Include(d => d.RetentionPolicy)
            .Where(d => d.IsArchived && d.ArchiveTier == DocumentArchiveTier.Warm && !d.LegalHold)
            .Where(d => d.ArchivedAtUtc != null)
            .ToListAsync(ct);

        foreach (var doc in warmArchived)
        {
            var coldDays = doc.RetentionPolicy?.MoveToColdAfterDays;
            if (coldDays is null or <= 0) continue;
            if (doc.ArchivedAtUtc!.Value.AddDays(coldDays.Value) > now) continue;
            doc.ArchiveTier = DocumentArchiveTier.Cold;
            doc.UpdatedAtUtc = now;
            db.DocumentActivities.Add(new DocumentActivity
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                EventType = "Archive",
                Message = "انتقال خودکار به بایگانی سرد",
                CreatedAtUtc = now,
            });
            actions++;
        }

        var toDelete = await db.Documents
            .Where(d => !d.LegalHold && !d.LongTermRetention)
            .Where(d => d.ScheduledDeleteAtUtc != null && d.ScheduledDeleteAtUtc <= now)
            .ToListAsync(ct);

        foreach (var doc in toDelete)
        {
            doc.IsDeleted = true;
            doc.LifecycleStatus = DocumentLifecycleStatus.PendingDeletion;
            doc.UpdatedAtUtc = now;
            db.DocumentActivities.Add(new DocumentActivity
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                EventType = "Delete",
                Message = "حذف خودکار طبق سیاست نگهداری",
                CreatedAtUtc = now,
            });
            actions++;
        }

        if (actions > 0)
            await db.SaveChangesAsync(ct);

        return actions;
    }
}
