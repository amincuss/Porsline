using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.ContractTemplates;
using PorslineClone.Application.FormWordTemplates;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.FormWordTemplates;

public class FormWordTemplateService(
    AppDbContext db,
    IContractDocumentGenerator generator,
    FormWordTemplateFileStorage storage,
    IWebHostEnvironment env)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<IReadOnlyList<FormWordTemplateListItemDto>> ListAsync(CancellationToken ct = default)
    {
        var rows = await db.FormWordTemplates.AsNoTracking()
            .Where(x => !x.IsDeleted)
            .Include(x => x.Form)
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ToListAsync(ct);

        return rows.Select(MapListItem).ToList();
    }

    public async Task<FormWordTemplateDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.FormWordTemplates.AsNoTracking()
            .Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        return row is null ? null : await MapDetailAsync(row, ct);
    }

    public async Task<FormWordTemplateDetailDto?> GetByFormIdAsync(Guid formId, CancellationToken ct = default)
    {
        var row = await db.FormWordTemplates.AsNoTracking()
            .Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.FormId == formId && !x.IsDeleted, ct);
        return row is null ? null : await MapDetailAsync(row, ct);
    }

    public async Task<FormWordTemplateDetailDto> CreateAsync(Guid formId, string name, CancellationToken ct = default)
    {
        var form = await db.Forms.FirstOrDefaultAsync(x => x.Id == formId && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("فرم یافت نشد");

        var exists = await db.FormWordTemplates.AnyAsync(x => x.FormId == formId && !x.IsDeleted, ct);
        if (exists)
            throw new InvalidOperationException("برای این فرم قبلاً قالب تبدیل تعریف شده است");

        var now = DateTime.UtcNow;
        var entity = new FormWordTemplate
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Name = name.Trim(),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.FormWordTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        entity.Form = form;
        return (await MapDetailAsync(entity, ct))!;
    }

    public async Task<FormWordTemplateDetailDto> UploadDocxAsync(Guid id, IFormFile file, CancellationToken ct = default)
    {
        var entity = await db.FormWordTemplates.Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");

        if (!file.FileName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فقط فایل Word (.docx) مجاز است");

        var (rel, orig) = await storage.SaveDocxAsync(id, file, ct);
        var full = storage.ResolveFullPath(rel);
        var placeholders = generator.ScanPlaceholders(full);

        entity.DocxFilePath = rel;
        entity.DocxFileName = orig;
        entity.DetectedPlaceholdersJson = JsonSerializer.Serialize(placeholders, JsonOpts);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await MapDetailAsync(entity, ct))!;
    }

    public async Task<FormWordTemplateDetailDto> SaveMappingsAsync(
        Guid id,
        IReadOnlyList<FormWordFieldMappingDto> mappings,
        string? signaturePlaceholderKey,
        string? stampPlaceholderKey,
        CancellationToken ct = default)
    {
        var entity = await db.FormWordTemplates.Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");

        entity.FieldMappingsJson = JsonSerializer.Serialize(mappings, JsonOpts);
        entity.SignaturePlaceholderKey = string.IsNullOrWhiteSpace(signaturePlaceholderKey)
            ? null
            : signaturePlaceholderKey.Trim();
        entity.StampPlaceholderKey = string.IsNullOrWhiteSpace(stampPlaceholderKey)
            ? null
            : stampPlaceholderKey.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await MapDetailAsync(entity, ct))!;
    }

    public async Task<FormWordTemplateDetailDto> UploadSignatureAsync(Guid id, IFormFile file, CancellationToken ct = default)
    {
        var entity = await db.FormWordTemplates.Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");

        var rel = await storage.SaveSignatureAsync(id, file, ct);
        entity.SignatureImagePath = rel;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await MapDetailAsync(entity, ct))!;
    }

    public async Task<FormWordTemplateDetailDto> UploadStampAsync(Guid id, IFormFile file, CancellationToken ct = default)
    {
        var entity = await db.FormWordTemplates.Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");

        var rel = await storage.SaveStampAsync(id, file, ct);
        entity.StampImagePath = rel;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await MapDetailAsync(entity, ct))!;
    }

    /// <summary>حذف نرم — فرم از قالب جدا می‌شود و می‌توان قالب جدید برای همان فرم ساخت.</summary>
    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.FormWordTemplates
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (entity is null) return false;

        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<FormWordGroupedSubmissionsDto> GetGroupedSubmissionsAsync(
        Guid? formId,
        Guid? responderGroupId,
        bool ungroupedOnly,
        string? currentUserId,
        bool isAdmin,
        Guid currentUserGuid,
        CancellationToken ct = default)
    {
        var q = db.FormSubmissions.AsNoTracking()
            .Include(x => x.Form)
            .Where(x => x.Form != null && !x.Form.IsDeleted);

        if (formId is { } fid)
            q = q.Where(x => x.FormId == fid);

        if (!isAdmin && currentUserGuid != Guid.Empty)
        {
            q = q.Where(x =>
                x.Form!.UserId == currentUserId
                || (x.DispatchLinkId != null
                    && db.FormDispatchLinks.Any(l =>
                        l.Id == x.DispatchLinkId && l.SentByUserId == currentUserGuid)));
        }

        if (ungroupedOnly)
        {
            var inAnyGroup = db.ResponderGroupMembers.Select(m => m.ResponderId);
            q = q.Where(x => x.ResponderId == null || !inAnyGroup.Contains(x.ResponderId.Value));
        }
        else if (responderGroupId is { } gid && gid != Guid.Empty)
        {
            var memberIds = db.ResponderGroupMembers
                .Where(m => m.GroupId == gid)
                .Select(m => m.ResponderId);
            q = q.Where(x => x.ResponderId != null && memberIds.Contains(x.ResponderId.Value));
        }

        var submissions = await q.OrderByDescending(x => x.SubmittedAtUtc).ToListAsync(ct);
        var responderIds = submissions.Where(x => x.ResponderId.HasValue).Select(x => x.ResponderId!.Value).Distinct().ToList();

        var memberRows = responderIds.Count == 0
            ? []
            : await db.ResponderGroupMembers.AsNoTracking()
                .Include(x => x.Group)
                .Where(x => responderIds.Contains(x.ResponderId))
                .ToListAsync(ct);

        var groupByResponder = memberRows
            .GroupBy(x => x.ResponderId)
            .ToDictionary(g => g.Key, g => g.First());

        var submissionIds = submissions.Select(x => x.Id).ToList();
        var allDocs = submissionIds.Count == 0
            ? []
            : await db.FormSubmissionWordDocuments.AsNoTracking()
                .Where(x => submissionIds.Contains(x.SubmissionId))
                .OrderByDescending(x => x.GeneratedAtUtc)
                .ToListAsync(ct);
        var docBySubmission = allDocs
            .GroupBy(x => x.SubmissionId)
            .ToDictionary(g => g.Key, g => g.First());

        var groupMap = new Dictionary<string, (Guid? GroupId, string GroupName, List<FormWordGroupedMemberDto> Members)>(StringComparer.Ordinal);

        foreach (var s in submissions)
        {
            Guid? gid = null;
            var gname = "بدون گروه";
            if (s.ResponderId is { } rid && groupByResponder.TryGetValue(rid, out var mem))
            {
                gid = mem.GroupId;
                gname = mem.Group?.Name ?? "گروه";
            }

            var key = gid?.ToString("N") ?? "_none";
            if (!groupMap.ContainsKey(key))
                groupMap[key] = (gid, gname, []);

            docBySubmission.TryGetValue(s.Id, out var doc);
            groupMap[key].Members.Add(new FormWordGroupedMemberDto(
                s.Id,
                s.FormId,
                s.Form?.Title ?? "",
                s.SubmitterName ?? "—",
                s.SubmitterEmail,
                s.TrackingCode,
                s.SubmittedAtUtc.ToString("o"),
                s.Status.ToString(),
                doc?.Id,
                doc?.FileName,
                doc?.GeneratedAtUtc));
        }

        var groups = groupMap.Values
            .Select(x => new FormWordGroupedGroupDto(x.GroupId, x.GroupName, x.Members))
            .OrderBy(g => g.GroupName)
            .ToList();

        var templates = await ListAsync(ct);
        if (formId is { } f)
            templates = templates.Where(t => t.FormId == f).ToList();

        return new FormWordGroupedSubmissionsDto(groups, templates);
    }

    public async Task<IReadOnlyList<FormSubmissionWordDocument>> GenerateForSubmissionsAsync(
        Guid templateId,
        IReadOnlyList<Guid>? submissionIds,
        IReadOnlyList<WordImageOverrideDto>? imageOverrides = null,
        CancellationToken ct = default)
    {
        var template = await db.FormWordTemplates.Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == templateId && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب تبدیل یافت نشد");

        if (string.IsNullOrWhiteSpace(template.DocxFilePath))
            throw new InvalidOperationException("فایل Word قالب آپلود نشده است");

        var sourcePath = storage.ResolveFullPath(template.DocxFilePath);
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("فایل قالب Word روی سرور یافت نشد");

        var mappings = DeserializeMappings(template.FieldMappingsJson);
        if (mappings.Count == 0)
            throw new InvalidOperationException("نگاشت فیلدها به placeholder تنظیم نشده است");

        var q = db.FormSubmissions
            .Where(x => x.FormId == template.FormId && x.Form != null && !x.Form.IsDeleted);

        if (submissionIds is { Count: > 0 })
            q = q.Where(x => submissionIds.Contains(x.Id));

        var submissions = await q.ToListAsync(ct);
        if (submissions.Count == 0)
            throw new InvalidOperationException("پاسخی برای تولید سند یافت نشد");

        var formFieldDefs = await db.FormFields.AsNoTracking()
            .Where(f => f.FormId == template.FormId)
            .ToListAsync(ct);

        var overrideBySubmission = (imageOverrides ?? [])
            .GroupBy(x => x.SubmissionId)
            .ToDictionary(
                g => g.Key,
                g => g.ToDictionary(
                    x => NormalizeKey(x.PlaceholderKey),
                    x => x,
                    StringComparer.OrdinalIgnoreCase));

        var respondersById = await LoadRespondersForSubmissionsAsync(submissions, ct);

        var results = new List<FormSubmissionWordDocument>();
        foreach (var submission in submissions)
        {
            overrideBySubmission.TryGetValue(submission.Id, out var subOverrides);
            var values = BuildFieldValues(submission, template, mappings, formFieldDefs, subOverrides);
            var tempPath = await generator.GenerateDocxAsync(sourcePath, values, ct);
            try
            {
                Responder? responder = null;
                if (submission.ResponderId is { } rid)
                    respondersById.TryGetValue(rid, out responder);

                var exportBaseName = Path.GetFileNameWithoutExtension(
                    FormWordExportFileNameBuilder.BuildDocxFileName(submission, responder));
                var (rel, fileName) = await storage.SaveExportAsync(submission.Id, exportBaseName, tempPath, ct);

                var doc = new FormSubmissionWordDocument
                {
                    Id = Guid.NewGuid(),
                    SubmissionId = submission.Id,
                    TemplateId = template.Id,
                    FileName = fileName,
                    FilePath = rel,
                    GeneratedAtUtc = DateTime.UtcNow,
                };
                db.FormSubmissionWordDocuments.Add(doc);
                results.Add(doc);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* ignore */ }
            }
        }

        await db.SaveChangesAsync(ct);
        return results;
    }

    public async Task<(byte[] ZipBytes, string ZipFileName)> GenerateZipForSubmissionsAsync(
        Guid templateId,
        IReadOnlyList<Guid>? submissionIds,
        IReadOnlyList<WordImageOverrideDto>? imageOverrides = null,
        CancellationToken ct = default)
    {
        var docs = await GenerateForSubmissionsAsync(templateId, submissionIds, imageOverrides, ct);
        if (docs.Count == 0)
            throw new InvalidOperationException("فایلی برای فشرده‌سازی تولید نشد");

        return await BuildZipArchiveAsync(templateId, docs, ct);
    }

    /// <summary>فقط ZIP از آخرین فایل Word تولیدشده هر پاسخ (پس از مرحله تبدیل).</summary>
    public async Task<(byte[] ZipBytes, string ZipFileName)> PackZipFromLatestDocumentsAsync(
        Guid templateId,
        IReadOnlyList<Guid> submissionIds,
        CancellationToken ct = default)
    {
        if (submissionIds.Count == 0)
            throw new InvalidOperationException("پاسخی برای فشرده‌سازی انتخاب نشده است");

        var template = await db.FormWordTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == templateId && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب تبدیل یافت نشد");

        var allDocs = await db.FormSubmissionWordDocuments.AsNoTracking()
            .Where(x => x.TemplateId == templateId && submissionIds.Contains(x.SubmissionId))
            .OrderByDescending(x => x.GeneratedAtUtc)
            .ToListAsync(ct);

        var latestBySubmission = allDocs
            .GroupBy(x => x.SubmissionId)
            .ToDictionary(g => g.Key, g => g.First());

        var missing = submissionIds.Where(id => !latestBySubmission.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new InvalidOperationException(
                $"برای {missing.Count} نفر هنوز فایل Word تولید نشده — ابتدا تبدیل را کامل کنید");

        var docs = submissionIds.Select(id => latestBySubmission[id]).ToList();
        return await BuildZipArchiveAsync(templateId, docs, ct);
    }

    private async Task<(byte[] ZipBytes, string ZipFileName)> BuildZipArchiveAsync(
        Guid templateId,
        IReadOnlyList<FormSubmissionWordDocument> docs,
        CancellationToken ct)
    {
        var template = await db.FormWordTemplates.AsNoTracking()
            .Include(x => x.Form)
            .FirstAsync(x => x.Id == templateId, ct);

        var docSubmissionIds = docs.Select(d => d.SubmissionId).Distinct().ToList();
        var submissionsById = await db.FormSubmissions.AsNoTracking()
            .Where(s => docSubmissionIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        var respondersById = await LoadRespondersForSubmissionsAsync(submissionsById.Values, ct);

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addedCount = 0;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var doc in docs)
            {
                var full = storage.ResolveFullPath(doc.FilePath);
                if (!File.Exists(full)) continue;

                Responder? responder = null;
                if (submissionsById.TryGetValue(doc.SubmissionId, out var sub)
                    && sub.ResponderId is { } rid)
                    respondersById.TryGetValue(rid, out responder);

                var zipEntryName = sub is not null
                    ? FormWordExportFileNameBuilder.BuildDocxFileName(sub, responder)
                    : doc.FileName;
                var entryName = FormWordExportFileNameBuilder.EnsureUnique(zipEntryName, usedNames);
                var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(full);
                await fileStream.CopyToAsync(entryStream, ct);
                addedCount++;
            }
        }

        if (addedCount == 0)
            throw new InvalidOperationException("فایل Word تولیدشده روی سرور یافت نشد");

        var safeTemplate = FormWordTemplateFileStorage.SanitizeFileNamePublic(template.Name);
        var zipName = $"{safeTemplate}_{addedCount}nafar_{DateTime.UtcNow:yyyyMMdd_HHmm}.zip";
        return (ms.ToArray(), zipName);
    }

    private async Task<Dictionary<Guid, Responder>> LoadRespondersForSubmissionsAsync(
        IEnumerable<FormSubmission> submissions,
        CancellationToken ct)
    {
        var responderIds = submissions
            .Where(s => s.ResponderId.HasValue)
            .Select(s => s.ResponderId!.Value)
            .Distinct()
            .ToList();
        if (responderIds.Count == 0)
            return new Dictionary<Guid, Responder>();

        return await db.Responders.AsNoTracking()
            .Where(r => responderIds.Contains(r.Id) && !r.IsDeleted)
            .ToDictionaryAsync(r => r.Id, ct);
    }

    public string? ResolveExportFullPath(Guid documentId)
    {
        var doc = db.FormSubmissionWordDocuments.AsNoTracking().FirstOrDefault(x => x.Id == documentId);
        return doc is null ? null : storage.ResolveFullPath(doc.FilePath);
    }

    private Dictionary<string, string> BuildFieldValues(
        FormSubmission submission,
        FormWordTemplate template,
        List<FormWordFieldMappingDto> mappings,
        List<FormField> formFieldDefs,
        Dictionary<string, WordImageOverrideDto>? imageOverrides = null)
    {
        var fields = string.IsNullOrWhiteSpace(submission.FieldsJson)
            ? new List<FormFieldValueDto>()
            : (JsonSerializer.Deserialize<List<FormFieldValueDto>>(submission.FieldsJson) ?? []);

        var byLabel = fields
            .GroupBy(f => (f.Label ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Last().Value ?? "", StringComparer.OrdinalIgnoreCase);

        var defByLabel = formFieldDefs
            .GroupBy(f => (f.Label ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ff in formFieldDefs.Where(f => f.FieldType == FieldType.FixedConstant))
        {
            var ph = NormalizeKey(ff.Placeholder ?? "");
            if (string.IsNullOrEmpty(ph)) continue;
            result[ph] = ff.DefaultValue ?? "";
        }

        foreach (var m in mappings)
        {
            var key = NormalizeKey(m.PlaceholderKey);
            if (string.IsNullOrEmpty(key)) continue;

            if (string.Equals(m.Source, "adminSignature", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(template.SignatureImagePath)
                    && !string.IsNullOrWhiteSpace(template.SignaturePlaceholderKey)
                    && string.Equals(NormalizeKey(template.SignaturePlaceholderKey), key, StringComparison.OrdinalIgnoreCase))
                {
                    var imgVal = BuildSignatureImageValue(storage, template.SignatureImagePath);
                    if (imgVal is not null)
                        result[key] = imgVal;
                }
                continue;
            }

            if (string.Equals(m.Source, "adminStamp", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(template.StampImagePath)
                    && !string.IsNullOrWhiteSpace(template.StampPlaceholderKey)
                    && string.Equals(NormalizeKey(template.StampPlaceholderKey), key, StringComparison.OrdinalIgnoreCase))
                {
                    var imgVal = BuildSignatureImageValue(storage, template.StampImagePath);
                    if (imgVal is not null)
                        result[key] = imgVal;
                }
                continue;
            }

            if (string.Equals(m.Source, "fixed", StringComparison.OrdinalIgnoreCase))
            {
                result[key] = m.FixedValue ?? "";
                continue;
            }

            if (string.IsNullOrWhiteSpace(m.FormFieldLabel)) continue;
            var label = m.FormFieldLabel.Trim();

            if (defByLabel.TryGetValue(label, out var def) && def.FieldType == FieldType.FixedConstant)
            {
                result[key] = def.DefaultValue ?? "";
                continue;
            }

            if (imageOverrides is not null && imageOverrides.TryGetValue(key, out var ov))
            {
                var cropped = BuildImageJsonFromDataUrl(ov.DataUrl, ov.WidthPx);
                if (cropped is not null)
                {
                    result[key] = cropped;
                    continue;
                }
            }

            defByLabel.TryGetValue(label, out var fieldDef);
            if (fieldDef is not null && IsImageFieldType(fieldDef.FieldType)
                && byLabel.TryGetValue(label, out var uploadPath)
                && FormSubmissionUploadHelper.IsUploadPath(uploadPath))
            {
                var widthPx = m.ImageWidthPx ?? ContractTemplateImageValue.DefaultWidthPx;
                var imgVal = BuildImageJsonFromDisk(uploadPath, widthPx);
                if (imgVal is not null)
                {
                    result[key] = imgVal;
                    continue;
                }
            }

            if (byLabel.TryGetValue(label, out var val) && !FormSubmissionUploadHelper.IsUploadPath(val))
                result[key] = val ?? "";
        }

        return result;
    }

    private static bool IsImageFieldType(FieldType ft) =>
        ft is FieldType.ImageUpload or FieldType.PersonalPhoto;

    private string? BuildImageJsonFromDisk(string? storedPath, int widthPx)
    {
        if (!FormSubmissionUploadHelper.TryResolveDiskPath(env, storedPath, out var fullPath))
            return null;
        try
        {
            var bytes = File.ReadAllBytes(fullPath);
            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            var mime = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/png",
            };
            var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            return BuildImageJsonFromDataUrl(dataUrl, widthPx);
        }
        catch
        {
            return null;
        }
    }

    private static string? BuildImageJsonFromDataUrl(string dataUrl, int widthPx)
    {
        if (!ContractTemplateImageValue.TryParse(dataUrl, out var payload) && !dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return null;
        var w = ContractTemplateImageValue.ClampWidth(widthPx > 0 ? widthPx : ContractTemplateImageValue.DefaultWidthPx);
        if (payload is not null)
            return JsonSerializer.Serialize(new { dataUrl = payload.DataUrl, widthPx = w });
        return JsonSerializer.Serialize(new { dataUrl, widthPx = w });
    }

    private static string? BuildSignatureImageValue(FormWordTemplateFileStorage fileStorage, string relativeImagePath)
    {
        try
        {
            var full = fileStorage.ResolveFullPath(relativeImagePath);
            if (!File.Exists(full)) return null;
            var bytes = File.ReadAllBytes(full);
            var ext = Path.GetExtension(full).ToLowerInvariant();
            var mime = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                _ => "image/png",
            };
            var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
            return JsonSerializer.Serialize(new { dataUrl, widthPx = ContractTemplateImageValue.DefaultWidthPx });
        }
        catch
        {
            return null;
        }
    }

    private async Task<FormWordTemplateDetailDto> MapDetailAsync(FormWordTemplate row, CancellationToken ct)
    {
        var formFields = await db.FormFields.AsNoTracking()
            .Where(f => f.FormId == row.FormId)
            .OrderBy(f => f.SortOrder)
            .Select(f => new FormWordFormFieldOptionDto(
                f.Id,
                f.Label,
                (int)f.FieldType,
                f.FieldType == FieldType.FixedConstant ? f.Placeholder : null))
            .ToListAsync(ct);

        var placeholders = DeserializeStringList(row.DetectedPlaceholdersJson);
        return new FormWordTemplateDetailDto(
            row.Id,
            row.FormId,
            row.Form?.Title ?? "",
            row.Name,
            row.DocxFileName,
            placeholders,
            DeserializeMappings(row.FieldMappingsJson),
            row.SignaturePlaceholderKey,
            !string.IsNullOrWhiteSpace(row.SignatureImagePath),
            row.StampPlaceholderKey,
            !string.IsNullOrWhiteSpace(row.StampImagePath),
            formFields,
            row.UpdatedAtUtc);
    }

    private static FormWordTemplateListItemDto MapListItem(FormWordTemplate row)
    {
        var placeholders = DeserializeStringList(row.DetectedPlaceholdersJson);
        var mappings = DeserializeMappings(row.FieldMappingsJson);
        return new FormWordTemplateListItemDto(
            row.Id,
            row.FormId,
            row.Form?.Title ?? "",
            row.Name,
            placeholders.Count,
            !string.IsNullOrWhiteSpace(row.DocxFilePath),
            mappings.Count > 0,
            row.UpdatedAtUtc);
    }

    private static List<string> DeserializeStringList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static List<FormWordFieldMappingDto> DeserializeMappings(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<FormWordFieldMappingDto>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeKey(string key)
        => (key ?? "").Trim().ToLowerInvariant().Replace(" ", "_");
}
