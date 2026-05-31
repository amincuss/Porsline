using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.FormWordTemplates;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.FormWordTemplates;

public class FormWordBatchExportService(
    AppDbContext db,
    FormWordTemplateService wordTemplateService,
    FormWordTemplateFileStorage storage)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<FormWordBatchExportJob> CreateQueuedJobAsync(
        Guid templateId,
        IReadOnlyList<Guid> submissionIds,
        IReadOnlyList<WordImageOverrideDto>? imageOverrides,
        Guid? createdByUserId,
        CancellationToken ct = default)
    {
        if (submissionIds.Count == 0)
            throw new InvalidOperationException("هیچ پاسخی برای تبدیل انتخاب نشده است");

        var templateExists = await db.FormWordTemplates.AsNoTracking()
            .AnyAsync(x => x.Id == templateId && !x.IsDeleted, ct);
        if (!templateExists)
            throw new InvalidOperationException("قالب تبدیل یافت نشد");

        var job = new FormWordBatchExportJob
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            SubmissionIdsJson = JsonSerializer.Serialize(submissionIds, JsonOpts),
            ImageOverridesJson = imageOverrides is { Count: > 0 }
                ? JsonSerializer.Serialize(imageOverrides, JsonOpts)
                : null,
            Status = FormWordBatchExportStatus.Queued,
            TotalCount = submissionIds.Count,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.FormWordBatchExportJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }

    public async Task SetHangfireJobIdAsync(Guid jobId, string hangfireJobId, CancellationToken ct = default)
    {
        var job = await db.FormWordBatchExportJobs.FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new InvalidOperationException("کار یافت نشد");
        job.HangfireJobId = hangfireJobId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<FormWordBatchExportStatusDto?> GetStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.FormWordBatchExportJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == jobId, ct);
        return job is null ? null : MapStatus(job);
    }

    public async Task ExecuteBatchAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.FormWordBatchExportJobs.FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new InvalidOperationException("کار یافت نشد");

        job.Status = FormWordBatchExportStatus.Running;
        job.ProcessedCount = 0;
        job.ErrorMessage = null;
        await db.SaveChangesAsync(ct);

        try
        {
            var submissionIds = JsonSerializer.Deserialize<List<Guid>>(job.SubmissionIdsJson, JsonOpts) ?? [];
            if (submissionIds.Count == 0)
                throw new InvalidOperationException("لیست پاسخ‌ها خالی است");

            List<WordImageOverrideDto>? overrides = null;
            if (!string.IsNullOrWhiteSpace(job.ImageOverridesJson))
                overrides = JsonSerializer.Deserialize<List<WordImageOverrideDto>>(job.ImageOverridesJson, JsonOpts);

            await wordTemplateService.GenerateForSubmissionsAsync(
                job.TemplateId, submissionIds, overrides, ct);

            job.ProcessedCount = submissionIds.Count;
            await db.SaveChangesAsync(ct);

            var (zipBytes, zipName) = await wordTemplateService.PackZipFromLatestDocumentsAsync(
                job.TemplateId, submissionIds, ct);

            var (rel, fileName) = await storage.SaveBatchZipAsync(job.Id, zipName, zipBytes, ct);

            job.ZipFilePath = rel;
            job.ZipFileName = fileName;
            job.Status = FormWordBatchExportStatus.Completed;
            job.ProcessedCount = submissionIds.Count;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = FormWordBatchExportStatus.Failed;
            job.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public string? ResolveZipFullPath(Guid jobId)
    {
        var job = db.FormWordBatchExportJobs.AsNoTracking().FirstOrDefault(x => x.Id == jobId);
        if (job is null || string.IsNullOrWhiteSpace(job.ZipFilePath)) return null;
        return storage.ResolveFullPath(job.ZipFilePath);
    }

    private FormWordBatchExportStatusDto MapStatus(FormWordBatchExportJob job)
    {
        var status = job.Status switch
        {
            FormWordBatchExportStatus.Queued => "queued",
            FormWordBatchExportStatus.Running => "running",
            FormWordBatchExportStatus.Completed => "completed",
            FormWordBatchExportStatus.Failed => "failed",
            _ => "unknown",
        };

        long? zipSizeBytes = null;
        if (job.Status == FormWordBatchExportStatus.Completed && !string.IsNullOrWhiteSpace(job.ZipFilePath))
        {
            var full = storage.ResolveFullPath(job.ZipFilePath);
            if (File.Exists(full))
                zipSizeBytes = new FileInfo(full).Length;
        }

        return new FormWordBatchExportStatusDto(
            job.Id,
            status,
            job.TotalCount,
            job.ProcessedCount,
            job.ZipFileName,
            job.Status == FormWordBatchExportStatus.Completed
                ? $"/api/admin/user-forms/word-export-jobs/{job.Id}/download"
                : null,
            zipSizeBytes,
            job.ErrorMessage,
            job.CreatedAtUtc,
            job.CompletedAtUtc);
    }
}
