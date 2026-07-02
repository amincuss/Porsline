using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.FormSubmissions;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using static PorslineClone.Infrastructure.Services.FormSubmissions.FormSubmissionExcelExportSchema;

namespace PorslineClone.Infrastructure.Services.FormSubmissions;

public class FormSubmissionExcelExportService(
    AppDbContext db,
    FormSubmissionExcelExportFileStorage storage,
    ResponderGroupSmsInquiryService smsInquiry)
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public async Task<FormSubmissionExcelExportOptionsDto> GetOptionsAsync(
        Func<IQueryable<FormSubmission>> authorizedQuery,
        Guid? groupId,
        bool ungroupedOnly,
        CancellationToken ct = default)
    {
        if (!ungroupedOnly && (groupId is null || groupId == Guid.Empty))
            throw new InvalidOperationException("گروه را انتخاب کنید");

        var effectiveFormId = await smsInquiry.ResolveEffectiveFormIdForGroupFilterAsync(
            groupId, ungroupedOnly, null, ct);
        var q = ResponderGroupSubmissionFilter.Apply(db, authorizedQuery(), groupId, ungroupedOnly, effectiveFormId);
        var total = await q.CountAsync(ct);
        if (total == 0)
            return new FormSubmissionExcelExportOptionsDto(0, []);

        var formRows = await q
            .GroupBy(s => new { s.FormId, FormTitle = s.Form!.Title })
            .Select(g => new { g.Key.FormId, g.Key.FormTitle, Count = g.Count() })
            .OrderBy(x => x.FormTitle)
            .ToListAsync(ct);

        var forms = new List<FormSubmissionExcelExportFormOption>();
        foreach (var row in formRows)
        {
            var fieldsJson = await q
                .Where(s => s.FormId == row.FormId)
                .Select(s => s.FieldsJson)
                .ToListAsync(ct);

            var ctx = await LoadContextAsync(db, row.FormId, fieldsJson, ct);
            var columns = BuildAllColumns(ctx);

            var filledCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var json in fieldsJson)
            {
                foreach (var col in columns)
                {
                    if (CountFilled(col, json) == 0) continue;
                    filledCounts[col.Key] = filledCounts.GetValueOrDefault(col.Key) + 1;
                }
            }

            var fieldOptions = columns.Select(col => new FormSubmissionExcelExportFieldOption(
                col.Key,
                col.Header,
                col.Kind == ColumnKind.Meta,
                col.IsFile,
                col.Kind == ColumnKind.Meta ? row.Count : filledCounts.GetValueOrDefault(col.Key))).ToList();

            forms.Add(new FormSubmissionExcelExportFormOption(
                row.FormId,
                row.FormTitle,
                row.Count,
                fieldOptions));
        }

        return new FormSubmissionExcelExportOptionsDto(total, forms);
    }

    public async Task<FormSubmissionExcelExportJob> CreateQueuedJobAsync(
        Guid? groupId,
        bool ungroupedOnly,
        Guid formId,
        IReadOnlyList<string> selectedFieldKeys,
        Guid? createdByUserId,
        Func<IQueryable<FormSubmission>> authorizedQuery,
        CancellationToken ct = default)
    {
        if (selectedFieldKeys.Count == 0)
            throw new InvalidOperationException("حداقل یک فیلد برای خروجی انتخاب کنید");

        Guid? groupFormId = null;
        if (!ungroupedOnly && groupId is Guid gid && gid != Guid.Empty)
            groupFormId = formId;

        var q = ResponderGroupSubmissionFilter.Apply(db, authorizedQuery(), groupId, ungroupedOnly, groupFormId)
            .Where(s => s.FormId == formId);
        var total = await q.CountAsync(ct);
        if (total == 0)
            throw new InvalidOperationException("پاسخی برای خروجی یافت نشد");

        var job = new FormSubmissionExcelExportJob
        {
            Id = Guid.NewGuid(),
            GroupId = ungroupedOnly ? null : groupId,
            UngroupedOnly = ungroupedOnly,
            FormId = formId,
            SelectedFieldsJson = JsonSerializer.Serialize(selectedFieldKeys, JsonOpts),
            Status = FormSubmissionExcelExportStatus.Queued,
            TotalCount = total,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        db.FormSubmissionExcelExportJobs.Add(job);
        await db.SaveChangesAsync(ct);
        return job;
    }

    public async Task SetHangfireJobIdAsync(Guid jobId, string hangfireJobId, CancellationToken ct = default)
    {
        var job = await db.FormSubmissionExcelExportJobs.FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new InvalidOperationException("کار یافت نشد");
        job.HangfireJobId = hangfireJobId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<FormSubmissionExcelExportStatusDto?> GetStatusAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.FormSubmissionExcelExportJobs.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == jobId, ct);
        return job is null ? null : MapStatus(job);
    }

    public async Task ExecuteBatchAsync(Guid jobId, CancellationToken ct = default)
    {
        var job = await db.FormSubmissionExcelExportJobs.FirstOrDefaultAsync(x => x.Id == jobId, ct)
            ?? throw new InvalidOperationException("کار یافت نشد");

        job.Status = FormSubmissionExcelExportStatus.Running;
        job.ProcessedCount = 0;
        job.ErrorMessage = null;
        await db.SaveChangesAsync(ct);

        try
        {
            var selectedKeys = JsonSerializer.Deserialize<List<string>>(job.SelectedFieldsJson, JsonOpts) ?? [];
            if (selectedKeys.Count == 0)
                throw new InvalidOperationException("فیلدی انتخاب نشده است");

            Guid? groupFormId = null;
            if (!job.UngroupedOnly && job.GroupId is Guid gid && gid != Guid.Empty)
                groupFormId = job.FormId;

            var submissions = await ResponderGroupSubmissionFilter.Apply(
                    db,
                    ApplyAuthorizationFilter(
                        db.FormSubmissions.AsNoTracking()
                            .Include(s => s.Form)
                            .Where(s => s.FormId == job.FormId && s.Form != null && !s.Form.IsDeleted),
                        job.CreatedByUserId),
                    job.GroupId,
                    job.UngroupedOnly,
                    groupFormId)
                .OrderByDescending(s => s.SubmittedAtUtc)
                .ToListAsync(ct);

            if (submissions.Count == 0)
                throw new InvalidOperationException("پاسخی برای خروجی یافت نشد");

            var fieldsJson = submissions.Select(s => s.FieldsJson).ToList();
            var ctx = await LoadContextAsync(db, job.FormId, fieldsJson, ct);
            var columns = ResolveSelectedColumns(ctx, selectedKeys);
            if (columns.Count == 0)
                throw new InvalidOperationException("ستون معتبری برای خروجی یافت نشد");

            using var workbook = new XLWorkbook();
            var sheet = workbook.Worksheets.Add("پاسخ‌ها");
            sheet.RightToLeft = true;

            for (var col = 0; col < columns.Count; col++)
            {
                var headerCell = sheet.Cell(1, col + 1);
                headerCell.Value = columns[col].Header;
                headerCell.Style.Font.Bold = true;
                headerCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#EEF2FF");
            }

            var rowIndex = 2;
            foreach (var submission in submissions)
            {
                var valueIndex = SubmissionValueIndex.Parse(submission.FieldsJson);
                for (var col = 0; col < columns.Count; col++)
                {
                    sheet.Cell(rowIndex, col + 1).Value = ResolveCellValue(
                        submission,
                        valueIndex,
                        columns[col]);
                }

                job.ProcessedCount = rowIndex - 1;
                rowIndex++;

                if ((rowIndex - 2) % 25 == 0)
                    await db.SaveChangesAsync(ct);
            }

            sheet.Columns().AdjustToContents();
            sheet.SheetView.FreezeRows(1);

            await using var ms = new MemoryStream();
            workbook.SaveAs(ms);
            var fileName = $"{SanitizeFileStem(ctx.FormTitle)}_{DateTime.UtcNow:yyyyMMddHHmmss}.xlsx";
            var (rel, savedName) = await storage.SaveExcelAsync(job.Id, fileName, ms.ToArray(), ct);

            job.FilePath = rel;
            job.FileName = savedName;
            job.Status = FormSubmissionExcelExportStatus.Completed;
            job.ProcessedCount = submissions.Count;
            job.CompletedAtUtc = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            job.Status = FormSubmissionExcelExportStatus.Failed;
            job.ErrorMessage = ex.Message.Length > 2000 ? ex.Message[..2000] : ex.Message;
            job.CompletedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    public string? ResolveFileFullPath(Guid jobId)
    {
        var job = db.FormSubmissionExcelExportJobs.AsNoTracking().FirstOrDefault(x => x.Id == jobId);
        if (job is null || string.IsNullOrWhiteSpace(job.FilePath)) return null;
        return storage.ResolveFullPath(job.FilePath);
    }

    private static string SanitizeFileStem(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "user-forms" : cleaned;
    }

    private FormSubmissionExcelExportStatusDto MapStatus(FormSubmissionExcelExportJob job)
    {
        var status = job.Status switch
        {
            FormSubmissionExcelExportStatus.Queued => "queued",
            FormSubmissionExcelExportStatus.Running => "running",
            FormSubmissionExcelExportStatus.Completed => "completed",
            FormSubmissionExcelExportStatus.Failed => "failed",
            _ => "unknown",
        };

        long? sizeBytes = null;
        if (job.Status == FormSubmissionExcelExportStatus.Completed && !string.IsNullOrWhiteSpace(job.FilePath))
        {
            var full = storage.ResolveFullPath(job.FilePath);
            if (full is not null && File.Exists(full))
                sizeBytes = new FileInfo(full).Length;
        }

        return new FormSubmissionExcelExportStatusDto(
            job.Id,
            status,
            job.TotalCount,
            job.ProcessedCount,
            job.FileName,
            job.Status == FormSubmissionExcelExportStatus.Completed
                ? $"/api/admin/user-forms/excel-export-jobs/{job.Id}/download"
                : null,
            sizeBytes,
            job.ErrorMessage,
            job.CreatedAtUtc,
            job.CompletedAtUtc);
    }

    private IQueryable<FormSubmission> ApplyAuthorizationFilter(
        IQueryable<FormSubmission> q,
        Guid? createdByUserId)
    {
        if (createdByUserId is null || createdByUserId == Guid.Empty)
            return q;

        var isAdmin = db.UserRoles.Any(ur =>
            ur.UserId == createdByUserId
            && db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Admin"));

        if (isAdmin)
            return q;

        return q.Where(x =>
            x.Form!.UserId == createdByUserId.Value.ToString()
            || (x.DispatchLinkId != null
                && db.FormDispatchLinks.Any(l =>
                    l.Id == x.DispatchLinkId && l.SentByUserId == createdByUserId)));
    }
}
