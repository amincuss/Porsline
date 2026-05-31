using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services.Documents;

namespace PorslineClone.Api.Controllers;

[ApiController]
[Route("api/admin/document-lifecycle")]
[Authorize]
public class AdminDocumentLifecycleController(
    AppDbContext db,
    DocumentLifecycleService lifecycle) : ControllerBase
{
    private bool IsAdmin => User.IsInRole("Admin");

    private bool CanReadLifecycle =>
        IsAdmin
        || User.HasClaim("permission", "documents.lifecycle.read")
        || User.HasClaim("permission", "documents.archive.read")
        || User.HasClaim("permission", "forms.read");

    private bool CanUpdateLifecycle =>
        IsAdmin
        || User.HasClaim("permission", "documents.lifecycle.update")
        || User.HasClaim("permission", "forms.update");

    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(raw, out userId);
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        if (!CanReadLifecycle) return Forbid();
        var settings = await lifecycle.GetOrCreateSettingsAsync(ct);
        return Ok(new
        {
            settings.Id,
            settings.DefaultRetentionPolicyId,
            settings.AutoProcessEnabled,
            settings.DefaultExpirationWarningDays,
            settings.ProcessIntervalHours,
            settings.UpdatedAtUtc,
        });
    }

    [HttpPatch("settings")]
    public async Task<IActionResult> UpdateSettings([FromBody] UpdateLifecycleSettingsRequest req, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        var settings = await lifecycle.GetOrCreateSettingsAsync(ct);

        if (req.DefaultRetentionPolicyId is not null)
        {
            if (req.DefaultRetentionPolicyId == Guid.Empty)
                settings.DefaultRetentionPolicyId = null;
            else
            {
                var exists = await db.DocumentRetentionPolicies.AnyAsync(x => x.Id == req.DefaultRetentionPolicyId, ct);
                if (!exists) return BadRequest(new { message = "سیاست نگهداری پیش‌فرض یافت نشد" });
                settings.DefaultRetentionPolicyId = req.DefaultRetentionPolicyId;
            }
        }

        if (req.AutoProcessEnabled.HasValue)
            settings.AutoProcessEnabled = req.AutoProcessEnabled.Value;
        if (req.DefaultExpirationWarningDays.HasValue)
            settings.DefaultExpirationWarningDays = Math.Clamp(req.DefaultExpirationWarningDays.Value, 1, 365);
        if (req.ProcessIntervalHours.HasValue)
            settings.ProcessIntervalHours = Math.Clamp(req.ProcessIntervalHours.Value, 1, 168);

        settings.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "تنظیمات چرخه عمر ذخیره شد" });
    }

    [HttpGet("policies")]
    public async Task<IActionResult> ListPolicies(CancellationToken ct)
    {
        if (!CanReadLifecycle) return Forbid();
        var items = await db.DocumentRetentionPolicies.AsNoTracking()
            .OrderBy(x => x.SortOrder).ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Description,
                x.CategoryMatch,
                accessLevelMatch = x.AccessLevelMatch.HasValue ? x.AccessLevelMatch.Value.ToString() : null,
                x.ArchiveAfterDays,
                x.MoveToColdAfterDays,
                x.DeleteAfterDays,
                x.ExpirationWarningDays,
                x.LongTermRetention,
                x.IsActive,
                x.IsDefault,
                x.SortOrder,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
            })
            .ToListAsync(ct);
        return Ok(items);
    }

    [HttpPost("policies")]
    public async Task<IActionResult> CreatePolicy([FromBody] UpsertRetentionPolicyRequest req, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "نام سیاست الزامی است" });
        if (await db.DocumentRetentionPolicies.AnyAsync(x => x.Name == name, ct))
            return BadRequest(new { message = "سیاستی با این نام وجود دارد" });

        if (req.IsDefault == true)
            await ClearDefaultPolicyFlagAsync(ct);

        var policy = BuildPolicy(new DocumentRetentionPolicy { Id = Guid.NewGuid() }, req, name);
        policy.CreatedAtUtc = DateTime.UtcNow;
        db.DocumentRetentionPolicies.Add(policy);
        await db.SaveChangesAsync(ct);
        return Ok(new { policy.Id, message = "سیاست نگهداری ایجاد شد" });
    }

    [HttpPatch("policies/{id:guid}")]
    public async Task<IActionResult> UpdatePolicy(Guid id, [FromBody] UpsertRetentionPolicyRequest req, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        var policy = await db.DocumentRetentionPolicies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (policy is null) return NotFound(new { message = "سیاست یافت نشد" });

        var name = (req.Name ?? policy.Name).Trim();
        if (string.IsNullOrWhiteSpace(name)) return BadRequest(new { message = "نام سیاست الزامی است" });
        if (await db.DocumentRetentionPolicies.AnyAsync(x => x.Id != id && x.Name == name, ct))
            return BadRequest(new { message = "سیاستی با این نام وجود دارد" });

        if (req.IsDefault == true)
            await ClearDefaultPolicyFlagAsync(ct);

        BuildPolicy(policy, req, name);
        policy.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { policy.Id, message = "سیاست نگهداری به‌روزرسانی شد" });
    }

    [HttpDelete("policies/{id:guid}")]
    public async Task<IActionResult> DeletePolicy(Guid id, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        var policy = await db.DocumentRetentionPolicies.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (policy is null) return NotFound(new { message = "سیاست یافت نشد" });

        var inUse = await db.Documents.AnyAsync(x => x.RetentionPolicyId == id, ct);
        if (inUse) return BadRequest(new { message = "این سیاست به اسناد متصل است و قابل حذف نیست" });

        var settings = await db.DocumentLifecycleSettings.FirstOrDefaultAsync(ct);
        if (settings?.DefaultRetentionPolicyId == id)
            settings.DefaultRetentionPolicyId = null;

        db.DocumentRetentionPolicies.Remove(policy);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "سیاست حذف شد" });
    }

    [HttpGet("archive")]
    public async Task<IActionResult> ArchiveList(
        [FromQuery] string? q,
        [FromQuery] string? tier,
        [FromQuery] bool? legalHold,
        [FromQuery] bool? obsolete,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!CanReadLifecycle) return Forbid();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 5, 100);

        var query = db.Documents.AsNoTracking()
            .Where(x => x.IsArchived || x.LifecycleStatus == DocumentLifecycleStatus.Archived);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var like = $"%{q.Trim()}%";
            query = query.Where(x =>
                EF.Functions.Like(x.Title, like)
                || EF.Functions.Like(x.Category, like)
                || EF.Functions.Like(x.ReferenceNumber ?? "", like));
        }

        if (!string.IsNullOrWhiteSpace(tier) && Enum.TryParse<DocumentArchiveTier>(tier, true, out var tierEnum))
            query = query.Where(x => x.ArchiveTier == tierEnum);

        if (legalHold == true) query = query.Where(x => x.LegalHold);
        if (legalHold == false) query = query.Where(x => !x.LegalHold);
        if (obsolete == true) query = query.Where(x => x.IsObsolete);
        if (obsolete == false) query = query.Where(x => !x.IsObsolete);

        var total = await query.CountAsync(ct);
        var rows = await query
            .OrderByDescending(x => x.ArchivedAtUtc ?? x.UpdatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Category,
                x.ReferenceNumber,
                archiveTier = x.ArchiveTier.ToString(),
                lifecycleStatus = x.LifecycleStatus.ToString(),
                x.IsArchived,
                x.ArchivedAtUtc,
                x.LegalHold,
                x.LegalHoldReason,
                x.IsObsolete,
                x.ObsoleteAtUtc,
                x.ExpiresAtUtc,
                x.ScheduledDeleteAtUtc,
                x.LongTermRetention,
                x.UpdatedAtUtc,
                retentionPolicyName = x.RetentionPolicy != null ? x.RetentionPolicy.Name : null,
            })
            .ToListAsync(ct);

        return Ok(new
        {
            items = rows,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(total / (double)pageSize),
        });
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> LifecycleAlerts([FromQuery] int days = 30, [FromQuery] int archiveDays = 7, CancellationToken ct = default)
    {
        if (!CanReadLifecycle) return Forbid();
        days = Math.Clamp(days, 1, 365);
        archiveDays = Math.Clamp(archiveDays, 1, 90);
        var now = DateTime.UtcNow;
        var expiryThreshold = now.AddDays(days);
        var archiveThreshold = now.AddDays(archiveDays);

        var visible = db.Documents.AsNoTracking().Where(x => !x.IsDeleted);

        static object MapAlertRow(Guid id, string title, string category, DateTime? dueAt, string alertType, string? extra = null) =>
            new { id, title, category, dueAtUtc = dueAt, alertType, detail = extra };

        var expiringSoon = await visible
            .Where(x => !x.IsArchived && !x.LongTermRetention)
            .Where(x =>
                (x.ExpiresAtUtc != null && x.ExpiresAtUtc > now && x.ExpiresAtUtc <= expiryThreshold)
                || (x.ScheduledDeleteAtUtc != null && x.ScheduledDeleteAtUtc > now && x.ScheduledDeleteAtUtc <= expiryThreshold))
            .OrderBy(x => x.ExpiresAtUtc ?? x.ScheduledDeleteAtUtc)
            .Take(50)
            .Select(x => new { x.Id, x.Title, x.Category, x.ExpiresAtUtc, x.ScheduledDeleteAtUtc })
            .ToListAsync(ct);

        var expired = await visible
            .Where(x => !x.LongTermRetention)
            .Where(x =>
                (x.ExpiresAtUtc != null && x.ExpiresAtUtc <= now)
                || (x.ScheduledDeleteAtUtc != null && x.ScheduledDeleteAtUtc <= now))
            .OrderByDescending(x => x.ExpiresAtUtc ?? x.ScheduledDeleteAtUtc)
            .Take(50)
            .Select(x => new { x.Id, x.Title, x.Category, x.ExpiresAtUtc, x.ScheduledDeleteAtUtc, x.IsArchived })
            .ToListAsync(ct);

        var needsWorkflowReview = await visible
            .Where(x => !x.IsArchived && !x.IsObsolete)
            .Where(x =>
                x.WorkflowStatus == DocumentWorkflowStatus.Pending
                || x.WorkflowStatus == DocumentWorkflowStatus.InProgress
                || x.WorkflowStatus == DocumentWorkflowStatus.Rejected)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .Take(50)
            .Select(x => new { x.Id, x.Title, x.Category, workflowStatus = x.WorkflowStatus.ToString(), x.WorkflowName })
            .ToListAsync(ct);

        var scheduledArchiveSoon = await visible
            .Where(x => !x.IsArchived && !x.LegalHold && !x.IsObsolete)
            .Where(x => x.ScheduledArchiveAtUtc != null && x.ScheduledArchiveAtUtc > now && x.ScheduledArchiveAtUtc <= archiveThreshold)
            .OrderBy(x => x.ScheduledArchiveAtUtc)
            .Take(50)
            .Select(x => new { x.Id, x.Title, x.Category, x.ScheduledArchiveAtUtc })
            .ToListAsync(ct);

        var obsoleteMarked = await visible
            .Where(x => x.IsObsolete)
            .OrderByDescending(x => x.ObsoleteAtUtc ?? x.UpdatedAtUtc)
            .Take(30)
            .Select(x => new { x.Id, x.Title, x.Category, x.ObsoleteReason, x.ObsoleteAtUtc })
            .ToListAsync(ct);

        return Ok(new
        {
            warningDays = days,
            archiveWarningDays = archiveDays,
            summary = new
            {
                expiringSoon = expiringSoon.Count,
                expired = expired.Count,
                needsWorkflowReview = needsWorkflowReview.Count,
                scheduledArchiveSoon = scheduledArchiveSoon.Count,
                obsolete = obsoleteMarked.Count,
            },
            expiringSoon = expiringSoon.Select(x => MapAlertRow(
                x.Id, x.Title, x.Category, x.ExpiresAtUtc ?? x.ScheduledDeleteAtUtc, "expiring_soon")),
            expired = expired.Select(x => MapAlertRow(
                x.Id, x.Title, x.Category, x.ExpiresAtUtc ?? x.ScheduledDeleteAtUtc, "expired",
                x.IsArchived ? "archived" : null)),
            needsWorkflowReview = needsWorkflowReview.Select(x => new
            {
                x.Id,
                x.Title,
                x.Category,
                x.workflowStatus,
                workflowName = x.WorkflowName,
                alertType = "workflow_review",
            }),
            scheduledArchiveSoon = scheduledArchiveSoon.Select(x => MapAlertRow(
                x.Id, x.Title, x.Category, x.ScheduledArchiveAtUtc, "archive_soon")),
            obsolete = obsoleteMarked.Select(x => new
            {
                x.Id,
                x.Title,
                x.Category,
                x.ObsoleteReason,
                obsoleteAtUtc = x.ObsoleteAtUtc,
                alertType = "obsolete",
            }),
        });
    }

    [HttpGet("expiring-soon")]
    public async Task<IActionResult> ExpiringSoon([FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (!CanReadLifecycle) return Forbid();
        days = Math.Clamp(days, 1, 365);
        var threshold = DateTime.UtcNow.AddDays(days);

        var items = await db.Documents.AsNoTracking()
            .Where(x => !x.IsDeleted && !x.LegalHold)
            .Where(x =>
                (x.ExpiresAtUtc != null && x.ExpiresAtUtc <= threshold)
                || (x.ScheduledDeleteAtUtc != null && x.ScheduledDeleteAtUtc <= threshold))
            .OrderBy(x => x.ExpiresAtUtc ?? x.ScheduledDeleteAtUtc)
            .Take(100)
            .Select(x => new
            {
                x.Id,
                x.Title,
                x.Category,
                x.ExpiresAtUtc,
                x.ScheduledDeleteAtUtc,
                x.LegalHold,
                x.LongTermRetention,
                x.IsArchived,
                archiveTier = x.ArchiveTier.ToString(),
            })
            .ToListAsync(ct);

        return Ok(new { days, items });
    }

    [HttpPatch("documents/{id:guid}")]
    public async Task<IActionResult> UpdateDocumentLifecycle(Guid id, [FromBody] UpdateDocumentLifecycleRequest req, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        var doc = await db.Documents.Include(x => x.RetentionPolicy).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });

        var settings = await lifecycle.GetOrCreateSettingsAsync(ct);

        if (req.RetentionPolicyId.HasValue)
        {
            if (req.RetentionPolicyId == Guid.Empty)
            {
                doc.RetentionPolicyId = null;
                lifecycle.RecalculateSchedule(doc, null, settings.DefaultExpirationWarningDays);
            }
            else
            {
                var policy = await db.DocumentRetentionPolicies.FirstOrDefaultAsync(x => x.Id == req.RetentionPolicyId && x.IsActive, ct);
                if (policy is null) return BadRequest(new { message = "سیاست نگهداری یافت نشد" });
                doc.RetentionPolicyId = policy.Id;
                if (policy.LongTermRetention) doc.LongTermRetention = true;
                lifecycle.RecalculateSchedule(doc, policy, settings.DefaultExpirationWarningDays);
            }
        }

        if (req.ExpiresAtUtc.HasValue)
        {
            doc.ExpiresAtUtc = req.ExpiresAtUtc;
            lifecycle.RecalculateSchedule(doc, doc.RetentionPolicy, settings.DefaultExpirationWarningDays);
        }
        else if (req.ClearExpiresAt == true)
        {
            doc.ExpiresAtUtc = null;
            lifecycle.RecalculateSchedule(doc, doc.RetentionPolicy, settings.DefaultExpirationWarningDays);
        }

        if (req.LongTermRetention.HasValue)
        {
            doc.LongTermRetention = req.LongTermRetention.Value;
            lifecycle.RecalculateSchedule(doc, doc.RetentionPolicy, settings.DefaultExpirationWarningDays);
        }

        doc.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "چرخه عمر سند به‌روزرسانی شد" });
    }

    [HttpPost("documents/{id:guid}/archive")]
    public async Task<IActionResult> ArchiveDocument(Guid id, [FromBody] ArchiveDocumentRequest? req, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });
        if (doc.LegalHold) return BadRequest(new { message = "سند در Legal Hold است" });

        var tier = DocumentArchiveTier.Warm;
        if (req?.Tier is not null && Enum.TryParse<DocumentArchiveTier>(req.Tier, true, out var parsed))
            tier = parsed;

        lifecycle.ArchiveDocument(doc, tier, userId);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "سند بایگانی شد" });
    }

    [HttpPost("documents/{id:guid}/restore")]
    public async Task<IActionResult> RestoreDocument(Guid id, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });
        if (!doc.IsArchived) return BadRequest(new { message = "سند بایگانی نشده است" });

        lifecycle.RestoreFromArchive(doc, userId);
        var settings = await lifecycle.GetOrCreateSettingsAsync(ct);
        if (doc.RetentionPolicyId.HasValue)
        {
            var policy = await db.DocumentRetentionPolicies.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == doc.RetentionPolicyId, ct);
            lifecycle.RecalculateSchedule(doc, policy, settings.DefaultExpirationWarningDays);
        }

        await db.SaveChangesAsync(ct);
        return Ok(new { message = "سند از بایگانی بازیابی شد" });
    }

    [HttpPost("documents/{id:guid}/legal-hold")]
    public async Task<IActionResult> SetLegalHold(Guid id, [FromBody] LegalHoldRequest req, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });

        lifecycle.SetLegalHold(doc, true, req.Reason, userId);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Legal Hold فعال شد" });
    }

    [HttpDelete("documents/{id:guid}/legal-hold")]
    public async Task<IActionResult> ReleaseLegalHold(Guid id, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });
        if (!doc.LegalHold) return BadRequest(new { message = "Legal Hold فعال نیست" });

        lifecycle.SetLegalHold(doc, false, null, userId);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "Legal Hold برداشته شد" });
    }

    [HttpPost("documents/{id:guid}/obsolete")]
    public async Task<IActionResult> MarkObsolete(Guid id, [FromBody] ObsoleteRequest? req, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });

        lifecycle.MarkObsolete(doc, req?.Reason, userId);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "سند منسوخ شد" });
    }

    [HttpDelete("documents/{id:guid}/obsolete")]
    public async Task<IActionResult> ClearObsolete(Guid id, CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        if (!TryGetUserId(out var userId)) return Unauthorized();

        var doc = await db.Documents.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (doc is null) return NotFound(new { message = "سند یافت نشد" });
        if (!doc.IsObsolete) return BadRequest(new { message = "سند منسوخ نیست" });

        lifecycle.ClearObsolete(doc, userId);
        await db.SaveChangesAsync(ct);
        return Ok(new { message = "وضعیت منسوخ برداشته شد" });
    }

    [HttpPost("process-now")]
    public async Task<IActionResult> ProcessNow(CancellationToken ct)
    {
        if (!CanUpdateLifecycle) return Forbid();
        var count = await lifecycle.ProcessDueDocumentsAsync(ct);
        return Ok(new { message = "پردازش انجام شد", processed = count });
    }

    private async Task ClearDefaultPolicyFlagAsync(CancellationToken ct)
    {
        var defaults = await db.DocumentRetentionPolicies.Where(x => x.IsDefault).ToListAsync(ct);
        foreach (var p in defaults)
            p.IsDefault = false;
    }

    private static DocumentRetentionPolicy BuildPolicy(DocumentRetentionPolicy policy, UpsertRetentionPolicyRequest req, string name)
    {
        policy.Name = name;
        if (req.Description is not null) policy.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (req.CategoryMatch is not null) policy.CategoryMatch = string.IsNullOrWhiteSpace(req.CategoryMatch) ? null : req.CategoryMatch.Trim();
        if (req.AccessLevelMatch is not null)
        {
            policy.AccessLevelMatch = string.IsNullOrWhiteSpace(req.AccessLevelMatch)
                ? null
                : Enum.TryParse<DocumentAccessLevel>(req.AccessLevelMatch, true, out var level) ? level : policy.AccessLevelMatch;
        }
        if (req.ArchiveAfterDays.HasValue) policy.ArchiveAfterDays = req.ArchiveAfterDays;
        if (req.MoveToColdAfterDays.HasValue) policy.MoveToColdAfterDays = req.MoveToColdAfterDays;
        if (req.DeleteAfterDays.HasValue) policy.DeleteAfterDays = req.DeleteAfterDays;
        if (req.ExpirationWarningDays.HasValue) policy.ExpirationWarningDays = Math.Clamp(req.ExpirationWarningDays.Value, 1, 365);
        if (req.LongTermRetention.HasValue) policy.LongTermRetention = req.LongTermRetention.Value;
        if (req.IsActive.HasValue) policy.IsActive = req.IsActive.Value;
        if (req.IsDefault.HasValue) policy.IsDefault = req.IsDefault.Value;
        if (req.SortOrder.HasValue) policy.SortOrder = req.SortOrder.Value;
        policy.UpdatedAtUtc = DateTime.UtcNow;
        return policy;
    }
}

public sealed class UpdateLifecycleSettingsRequest
{
    public Guid? DefaultRetentionPolicyId { get; set; }
    public bool? AutoProcessEnabled { get; set; }
    public int? DefaultExpirationWarningDays { get; set; }
    public int? ProcessIntervalHours { get; set; }
}

public sealed class UpsertRetentionPolicyRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? CategoryMatch { get; set; }
    public string? AccessLevelMatch { get; set; }
    public int? ArchiveAfterDays { get; set; }
    public int? MoveToColdAfterDays { get; set; }
    public int? DeleteAfterDays { get; set; }
    public int? ExpirationWarningDays { get; set; }
    public bool? LongTermRetention { get; set; }
    public bool? IsActive { get; set; }
    public bool? IsDefault { get; set; }
    public int? SortOrder { get; set; }
}

public sealed class UpdateDocumentLifecycleRequest
{
    public Guid? RetentionPolicyId { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
    public bool? ClearExpiresAt { get; set; }
    public bool? LongTermRetention { get; set; }
}

public sealed class ArchiveDocumentRequest
{
    public string? Tier { get; set; }
}

public sealed class LegalHoldRequest
{
    public string? Reason { get; set; }
}

public sealed class ObsoleteRequest
{
    public string? Reason { get; set; }
}
