using System.IO.Compression;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.ContractTemplates;
using PorslineClone.Application.Contracts;
using PorslineClone.Application.FormWordTemplates;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;
using PorslineClone.Infrastructure.Services;
using PorslineClone.Infrastructure.Services.ContractTemplates;

namespace PorslineClone.Infrastructure.Services.FormWordTemplates;

public class FormWordTemplateService(
    AppDbContext db,
    FormWordTemplateFileStorage storage,
    IContractDocumentGenerator generator,
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

    public async Task<FormWordTemplateDetailDto?> GetByFormIdAsync(Guid formId, CancellationToken ct = default)
    {
        var row = await db.FormWordTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.FormId == formId && !x.IsDeleted, ct);
        return row is null ? null : await GetAsync(row.Id, ct);
    }

    public async Task<FormWordTemplateDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var row = await db.FormWordTemplates.AsNoTracking()
            .Include(x => x.Form)
            .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        return row is null ? null : await MapDetailAsync(row, ct);
    }

    public async Task<FormWordTemplateDetailDto> CreateAsync(Guid formId, string name, CancellationToken ct = default)
    {
        var form = await db.Forms.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == formId && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("فرم یافت نشد");

        if (await db.FormWordTemplates.AnyAsync(x => x.FormId == formId && !x.IsDeleted, ct))
            throw new InvalidOperationException("برای این فرم قبلاً قالب تبدیل Word تعریف شده است");

        var trashed = await db.FormWordTemplates.FirstOrDefaultAsync(x => x.FormId == formId && x.IsDeleted, ct);
        if (trashed is not null)
        {
            trashed.IsDeleted = false;
            trashed.Name = (name ?? "قالب تبدیل").Trim();
            trashed.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
            return (await GetAsync(trashed.Id, ct))!;
        }

        var now = DateTime.UtcNow;
        var entity = new FormWordTemplate
        {
            Id = Guid.NewGuid(),
            FormId = formId,
            Name = string.IsNullOrWhiteSpace(name) ? "قالب تبدیل" : name.Trim(),
            DetectedPlaceholdersJson = "[]",
            FieldMappingsJson = "[]",
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsDeleted = false,
        };
        db.FormWordTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(entity.Id, ct))!;
    }

    public async Task<FormWordTemplateDetailDto> UploadDocxAsync(Guid id, IFormFile file, CancellationToken ct = default)
    {
        ValidateDocx(file);
        var entity = await db.FormWordTemplates.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");

        var (rel, originalName) = await storage.SaveDocxAsync(id, file, ct);
        var full = storage.ResolveFullPath(rel);
        var placeholders = generator.ScanPlaceholders(full);

        entity.DocxFilePath = rel;
        entity.DocxFileName = originalName;
        entity.DetectedPlaceholdersJson = JsonSerializer.Serialize(placeholders, JsonOpts);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<FormWordTemplateDetailDto> SaveMappingsAsync(
        Guid id,
        IReadOnlyList<FormWordFieldMappingDto> mappings,
        string? signaturePlaceholderKey,
        string? stampPlaceholderKey,
        CancellationToken ct = default)
    {
        var entity = await db.FormWordTemplates.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");

        entity.FieldMappingsJson = JsonSerializer.Serialize(mappings ?? [], JsonOpts);
        entity.SignaturePlaceholderKey = string.IsNullOrWhiteSpace(signaturePlaceholderKey)
            ? null
            : signaturePlaceholderKey.Trim();
        entity.StampPlaceholderKey = string.IsNullOrWhiteSpace(stampPlaceholderKey)
            ? null
            : stampPlaceholderKey.Trim();
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<FormWordTemplateDetailDto> UploadSignatureAsync(Guid id, IFormFile file, CancellationToken ct = default)
    {
        ValidateImage(file);
        var entity = await db.FormWordTemplates.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");
        entity.SignatureImagePath = await storage.SaveSignatureAsync(id, file, ct);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<FormWordTemplateDetailDto> UploadStampAsync(Guid id, IFormFile file, CancellationToken ct = default)
    {
        ValidateImage(file);
        var entity = await db.FormWordTemplates.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");
        entity.StampImagePath = await storage.SaveStampAsync(id, file, ct);
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return (await GetAsync(id, ct))!;
    }

    public async Task<bool> SoftDeleteAsync(Guid id, CancellationToken ct = default)
    {
        var entity = await db.FormWordTemplates.FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);
        if (entity is null) return false;
        entity.IsDeleted = true;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<FormWordGroupedSubmissionsDto> GetGroupedSubmissionsAsync(
        Guid? formId,
        Guid? groupId,
        bool ungroupedOnly,
        string? currentUserId,
        bool isAdmin,
        Guid currentUserGuid,
        CancellationToken ct = default)
    {
        var templates = await ListAsync(ct);
        if (formId is { } fid && fid != Guid.Empty)
            templates = templates.Where(t => t.FormId == fid).ToList();

        var q = AuthorizedSubmissionsQuery(currentUserId, currentUserGuid, isAdmin);
        if (formId is { } formFilter && formFilter != Guid.Empty)
            q = q.Where(x => x.FormId == formFilter);

        q = ResponderGroupSubmissionFilter.Apply(
            db,
            q,
            groupId,
            ungroupedOnly,
            formId is Guid resolvedForm && resolvedForm != Guid.Empty ? resolvedForm : null);

        var submissions = await q
            .Include(x => x.Form)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ToListAsync(ct);

        if (submissions.Count == 0)
            return new FormWordGroupedSubmissionsDto([], templates);

        var submissionIds = submissions.Select(x => x.Id).ToList();
        var responderIds = submissions.Where(x => x.ResponderId != null).Select(x => x.ResponderId!.Value).Distinct().ToList();
        var responders = responderIds.Count == 0
            ? new Dictionary<Guid, Responder>()
            : await db.Responders.AsNoTracking()
                .Where(r => responderIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);

        var latestDocs = await LoadLatestWordDocumentsAsync(submissionIds, ct);

        var memberBySubmission = submissions.ToDictionary(
            s => s.Id,
            s =>
            {
                responders.TryGetValue(s.ResponderId ?? Guid.Empty, out var responder);
                latestDocs.TryGetValue(s.Id, out var doc);
                return MapGroupedMember(s, responder, doc);
            });

        var groups = new List<FormWordGroupedGroupDto>();

        if (ungroupedOnly)
        {
            groups.Add(new FormWordGroupedGroupDto(
                null,
                "بدون گروه",
                submissions.Select(s => memberBySubmission[s.Id]).ToList()));
        }
        else if (groupId is { } gid && gid != Guid.Empty)
        {
            var group = await db.ResponderGroups.AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == gid && !x.IsDeleted, ct);
            groups.Add(new FormWordGroupedGroupDto(
                gid,
                group?.Name ?? "گروه",
                submissions.Select(s => memberBySubmission[s.Id]).ToList()));
        }
        else
        {
            var memberships = await (
                from m in db.ResponderGroupMembers.AsNoTracking()
                join g in db.ResponderGroups.AsNoTracking() on m.GroupId equals g.Id
                where responderIds.Contains(m.ResponderId) && !g.IsDeleted && g.IsActive
                select new { m.ResponderId, m.GroupId, g.Name }
            ).ToListAsync(ct);

            var groupedResponderIds = memberships.Select(x => x.ResponderId).ToHashSet();
            var groupDefs = memberships
                .GroupBy(x => x.GroupId)
                .Select(g => new { GroupId = g.Key, Name = g.First().Name })
                .OrderBy(x => x.Name)
                .ToList();

            foreach (var g in groupDefs)
            {
                var memberResponderIds = memberships.Where(x => x.GroupId == g.GroupId).Select(x => x.ResponderId).ToHashSet();
                var members = submissions
                    .Where(s => s.ResponderId != null && memberResponderIds.Contains(s.ResponderId.Value))
                    .Select(s => memberBySubmission[s.Id])
                    .ToList();
                if (members.Count > 0)
                    groups.Add(new FormWordGroupedGroupDto(g.GroupId, g.Name, members));
            }

            var ungroupedMembers = submissions
                .Where(s => s.ResponderId == null || !groupedResponderIds.Contains(s.ResponderId.Value))
                .Select(s => memberBySubmission[s.Id])
                .ToList();
            if (ungroupedMembers.Count > 0)
                groups.Add(new FormWordGroupedGroupDto(null, "بدون گروه", ungroupedMembers));
        }

        return new FormWordGroupedSubmissionsDto(groups, templates);
    }

    public async Task<IReadOnlyList<FormSubmissionWordDocument>> GenerateForSubmissionsAsync(
        Guid templateId,
        IReadOnlyList<Guid> submissionIds,
        IReadOnlyList<WordImageOverrideDto>? imageOverrides,
        CancellationToken ct = default)
    {
        if (submissionIds.Count == 0)
            throw new InvalidOperationException("هیچ پاسخی انتخاب نشده است");

        var template = await LoadTemplateForGenerationAsync(templateId, ct);
        var mappings = DeserializeMappings(template.FieldMappingsJson);
        var form = await db.Forms.AsNoTracking()
            .Include(x => x.Fields)
            .FirstOrDefaultAsync(x => x.Id == template.FormId, ct)
            ?? throw new InvalidOperationException("فرم یافت نشد");

        var submissions = await db.FormSubmissions
            .Where(x => submissionIds.Contains(x.Id))
            .ToListAsync(ct);

        if (submissions.Count != submissionIds.Count)
            throw new InvalidOperationException("برخی پاسخ‌ها یافت نشد");

        var responderIds = submissions.Where(x => x.ResponderId != null).Select(x => x.ResponderId!.Value).Distinct().ToList();
        var responders = responderIds.Count == 0
            ? new Dictionary<Guid, Responder>()
            : await db.Responders.AsNoTracking()
                .Where(r => responderIds.Contains(r.Id))
                .ToDictionaryAsync(r => r.Id, ct);

        var results = new List<FormSubmissionWordDocument>();
        foreach (var submission in submissions)
        {
            if (submission.FormId != template.FormId)
                throw new InvalidOperationException("پاسخ انتخاب‌شده مربوط به فرم این قالب نیست");

            responders.TryGetValue(submission.ResponderId ?? Guid.Empty, out var responder);
            var fieldValues = BuildFieldValues(template, form, submission, responder, mappings, imageOverrides);
            var doc = await GenerateOneDocumentAsync(template, submission, responder, fieldValues, ct);
            results.Add(doc);
        }

        return results;
    }

    public async Task<(byte[] Bytes, string ZipFileName)> GenerateZipForSubmissionsAsync(
        Guid templateId,
        IReadOnlyList<Guid> submissionIds,
        IReadOnlyList<WordImageOverrideDto>? imageOverrides,
        CancellationToken ct = default)
    {
        await GenerateForSubmissionsAsync(templateId, submissionIds, imageOverrides, ct);
        return await PackZipFromLatestDocumentsAsync(templateId, submissionIds, ct);
    }

    public async Task<(byte[] Bytes, string ZipFileName)> PackZipFromLatestDocumentsAsync(
        Guid templateId,
        IReadOnlyList<Guid> submissionIds,
        CancellationToken ct = default)
    {
        if (submissionIds.Count == 0)
            throw new InvalidOperationException("هیچ پاسخی انتخاب نشده است");

        var template = await db.FormWordTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == templateId && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");

        var latestDocs = await LoadLatestWordDocumentsAsync(submissionIds, ct, templateId);
        if (latestDocs.Count == 0)
            throw new InvalidOperationException("فایل Word تولیدشده‌ای یافت نشد — ابتدا تبدیل را انجام دهید");

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var submissionId in submissionIds)
            {
                if (!latestDocs.TryGetValue(submissionId, out var doc)) continue;
                var full = storage.ResolveFullPath(doc.FilePath);
                if (!File.Exists(full)) continue;

                var entryName = FormWordExportFileNameBuilder.EnsureUnique(doc.FileName, usedNames);
                var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
                await using var entryStream = entry.Open();
                await using var fileStream = File.OpenRead(full);
                await fileStream.CopyToAsync(entryStream, ct);
            }
        }

        var zipName = FormWordTemplateFileStorage.SanitizeFileNamePublic($"{template.Name}_{DateTime.UtcNow:yyyyMMddHHmmss}.zip");
        return (ms.ToArray(), zipName);
    }

    public string? ResolveExportFullPath(Guid documentId)
    {
        var doc = db.FormSubmissionWordDocuments.AsNoTracking().FirstOrDefault(x => x.Id == documentId);
        if (doc is null || string.IsNullOrWhiteSpace(doc.FilePath)) return null;
        return storage.ResolveFullPath(doc.FilePath);
    }

    private async Task<FormSubmissionWordDocument> GenerateOneDocumentAsync(
        FormWordTemplate template,
        FormSubmission submission,
        Responder? responder,
        IReadOnlyDictionary<string, string> fieldValues,
        CancellationToken ct)
    {
        var sourcePath = storage.ResolveFullPath(template.DocxFilePath!);
        var tempPath = await generator.GenerateDocxAsync(sourcePath, fieldValues, ct);
        try
        {
            var downloadName = FormWordExportFileNameBuilder.BuildDocxFileName(submission, responder);
            var (rel, fileName) = await storage.SaveExportAsync(submission.Id, downloadName, tempPath, ct);
            var entity = new FormSubmissionWordDocument
            {
                Id = Guid.NewGuid(),
                SubmissionId = submission.Id,
                TemplateId = template.Id,
                FileName = fileName,
                FilePath = rel,
                GeneratedAtUtc = DateTime.UtcNow,
            };
            db.FormSubmissionWordDocuments.Add(entity);
            await db.SaveChangesAsync(ct);
            return entity;
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    private async Task<FormWordTemplate> LoadTemplateForGenerationAsync(Guid templateId, CancellationToken ct)
    {
        var template = await db.FormWordTemplates.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == templateId && !x.IsDeleted, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");

        if (string.IsNullOrWhiteSpace(template.DocxFilePath))
            throw new InvalidOperationException("ابتدا فایل Word قالب را بارگذاری کنید");

        var full = storage.ResolveFullPath(template.DocxFilePath);
        if (!File.Exists(full))
            throw new InvalidOperationException("فایل قالب Word روی دیسک یافت نشد");

        return template;
    }

    private Dictionary<string, string> BuildFieldValues(
        FormWordTemplate template,
        Form form,
        FormSubmission submission,
        Responder? responder,
        IReadOnlyList<FormWordFieldMappingDto> mappings,
        IReadOnlyList<WordImageOverrideDto>? imageOverrides)
    {
        var submissionValues = DeserializeSubmissionFields(submission.FieldsJson);
        var valuesByLabel = submissionValues
            .GroupBy(x => x.Label, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last().Value ?? "", StringComparer.Ordinal);

        var fieldTypesByLabel = form.Fields
            .GroupBy(x => x.Label, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().FieldType, StringComparer.Ordinal);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var m in mappings)
        {
            var key = NormalizeKey(m.PlaceholderKey);
            if (string.IsNullOrEmpty(key)) continue;

            var imageOverride = imageOverrides?.FirstOrDefault(o =>
                o.SubmissionId == submission.Id &&
                string.Equals(o.PlaceholderKey, m.PlaceholderKey, StringComparison.OrdinalIgnoreCase));
            if (imageOverride is not null)
            {
                result[key] = SerializeImageValue(imageOverride.DataUrl, imageOverride.WidthPx);
                continue;
            }

            if (string.Equals(m.Source, "adminSignature", StringComparison.OrdinalIgnoreCase))
            {
                result[key] = FilePathToImageJson(template.SignatureImagePath, m.ImageWidthPx ?? ContractTemplateImageValue.DefaultWidthPx) ?? "";
                continue;
            }

            if (string.Equals(m.Source, "adminStamp", StringComparison.OrdinalIgnoreCase))
            {
                result[key] = FilePathToImageJson(template.StampImagePath, m.ImageWidthPx ?? ContractTemplateImageValue.DefaultWidthPx) ?? "";
                continue;
            }

            if (string.Equals(m.Source, "fixed", StringComparison.OrdinalIgnoreCase))
            {
                result[key] = m.FixedValue ?? "";
                continue;
            }

            var label = m.FormFieldLabel?.Trim();
            if (string.IsNullOrEmpty(label)) continue;

            valuesByLabel.TryGetValue(label, out var rawValue);
            rawValue ??= "";

            fieldTypesByLabel.TryGetValue(label, out var fieldType);
            if ((fieldType is FieldType.ImageUpload or FieldType.PersonalPhoto)
                && FormSubmissionUploadHelper.IsUploadPath(rawValue))
            {
                result[key] = UploadPathToImageJson(rawValue, m.ImageWidthPx ?? ContractTemplateImageValue.DefaultWidthPx) ?? "";
            }
            else
            {
                result[key] = rawValue;
            }
        }

        foreach (var ff in form.Fields.Where(x => x.FieldType == FieldType.FixedConstant))
        {
            var key = NormalizeKey(ff.Placeholder);
            if (string.IsNullOrEmpty(key) || result.ContainsKey(key)) continue;
            result[key] = ff.DefaultValue ?? "";
        }

        return result;
    }

    private string? UploadPathToImageJson(string? uploadPath, int widthPx)
    {
        if (!FormSubmissionUploadHelper.TryResolveDiskPath(env, uploadPath, out var full))
            return null;
        return FileBytesToImageJson(full, widthPx);
    }

    private string? FilePathToImageJson(string? relativePath, int widthPx)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var full = storage.ResolveFullPath(relativePath);
        if (!File.Exists(full)) return null;
        return FileBytesToImageJson(full, widthPx);
    }

    private static string FileBytesToImageJson(string fullPath, int widthPx)
    {
        var bytes = File.ReadAllBytes(fullPath);
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var mime = ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            _ => "image/png",
        };
        var dataUrl = $"data:{mime};base64,{Convert.ToBase64String(bytes)}";
        return SerializeImageValue(dataUrl, widthPx);
    }

    private static string SerializeImageValue(string dataUrl, int widthPx) =>
        JsonSerializer.Serialize(new
        {
            dataUrl,
            widthPx = ContractTemplateImageValue.ClampWidth(widthPx),
        }, JsonOpts);

    private async Task<Dictionary<Guid, FormSubmissionWordDocument>> LoadLatestWordDocumentsAsync(
        IReadOnlyList<Guid> submissionIds,
        CancellationToken ct,
        Guid? templateId = null)
    {
        if (submissionIds.Count == 0) return [];

        var q = db.FormSubmissionWordDocuments.AsNoTracking()
            .Where(d => submissionIds.Contains(d.SubmissionId));
        if (templateId is { } tid && tid != Guid.Empty)
            q = q.Where(d => d.TemplateId == tid);

        var docs = await q.OrderByDescending(d => d.GeneratedAtUtc).ToListAsync(ct);
        return docs
            .GroupBy(d => d.SubmissionId)
            .ToDictionary(g => g.Key, g => g.First());
    }

    private IQueryable<FormSubmission> AuthorizedSubmissionsQuery(
        string? currentUserId,
        Guid currentUserGuid,
        bool isAdmin)
    {
        var q = db.FormSubmissions
            .Include(x => x.Form)
            .Where(x => x.Form != null && !x.Form.IsDeleted);

        if (!isAdmin && currentUserGuid != Guid.Empty)
        {
            q = q.Where(x =>
                x.Form!.UserId == currentUserId
                || (x.DispatchLinkId != null
                    && db.FormDispatchLinks.Any(l =>
                        l.Id == x.DispatchLinkId && l.SentByUserId == currentUserGuid)));
        }

        return q;
    }

    private static FormWordGroupedMemberDto MapGroupedMember(
        FormSubmission submission,
        Responder? responder,
        FormSubmissionWordDocument? latestDoc)
    {
        return new FormWordGroupedMemberDto(
            submission.Id,
            submission.FormId,
            submission.Form?.Title ?? "",
            submission.SubmitterName ?? responder?.FullName ?? "",
            submission.SubmitterEmail ?? responder?.MobileNumber,
            submission.TrackingCode,
            submission.SubmittedAtUtc.ToString("O"),
            ToClientStatus(submission.Status),
            latestDoc?.Id,
            latestDoc?.FileName,
            latestDoc?.GeneratedAtUtc);
    }

    private async Task<FormWordTemplateDetailDto> MapDetailAsync(FormWordTemplate row, CancellationToken ct)
    {
        var placeholders = DeserializePlaceholders(row.DetectedPlaceholdersJson);
        var mappings = DeserializeMappings(row.FieldMappingsJson);
        var formFields = await db.FormFields.AsNoTracking()
            .Where(x => x.FormId == row.FormId)
            .OrderBy(x => x.SortOrder)
            .Select(x => new FormWordFormFieldOptionDto(
                x.Id,
                x.Label,
                (int)x.FieldType,
                x.FieldType == FieldType.FixedConstant ? x.Placeholder : null))
            .ToListAsync(ct);

        return new FormWordTemplateDetailDto(
            row.Id,
            row.FormId,
            row.Form?.Title ?? "",
            row.Name,
            row.DocxFileName,
            placeholders,
            mappings,
            row.SignaturePlaceholderKey,
            !string.IsNullOrWhiteSpace(row.SignatureImagePath),
            row.StampPlaceholderKey,
            !string.IsNullOrWhiteSpace(row.StampImagePath),
            formFields,
            row.UpdatedAtUtc);
    }

    private static FormWordTemplateListItemDto MapListItem(FormWordTemplate row)
    {
        var placeholders = DeserializePlaceholders(row.DetectedPlaceholdersJson);
        var mappings = DeserializeMappings(row.FieldMappingsJson);
        var hasMappings = mappings.Any(m =>
            !string.IsNullOrWhiteSpace(m.FormFieldLabel)
            || !string.IsNullOrWhiteSpace(m.Source)
            || !string.IsNullOrWhiteSpace(m.FixedValue));

        return new FormWordTemplateListItemDto(
            row.Id,
            row.FormId,
            row.Form?.Title ?? "",
            row.Name,
            placeholders.Count,
            !string.IsNullOrWhiteSpace(row.DocxFilePath),
            hasMappings,
            row.UpdatedAtUtc);
    }

    private static List<string> DeserializePlaceholders(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
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
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<FormWordFieldMappingDto>>(json, JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static List<FormFieldValueDto> DeserializeSubmissionFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<FormFieldValueDto>>(json) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string NormalizeKey(string key) =>
        new string((key ?? "").Trim().Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray()).ToLowerInvariant();

    private static string ToClientStatus(FormSubmissionStatus status) => status switch
    {
        FormSubmissionStatus.Pending => "pending",
        FormSubmissionStatus.InProgress => "in_progress",
        FormSubmissionStatus.Approved => "approved",
        FormSubmissionStatus.Rejected => "rejected",
        FormSubmissionStatus.Submitted => "submitted",
        _ => "pending",
    };

    private static void ValidateDocx(IFormFile file)
    {
        if (file.Length == 0)
            throw new InvalidOperationException("فایل خالی است");
        if (file.Length > 15 * 1024 * 1024)
            throw new InvalidOperationException("حداکثر حجم قالب ۱۵ مگابایت است");
        var ext = Path.GetExtension(file.FileName);
        if (!string.Equals(ext, ".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فقط فایل Word (.docx) مجاز است");
    }

    private static void ValidateImage(IFormFile file)
    {
        if (file.Length == 0)
            throw new InvalidOperationException("فایل خالی است");
        if (file.Length > 5 * 1024 * 1024)
            throw new InvalidOperationException("حداکثر حجم تصویر ۵ مگابایت است");
    }
}
