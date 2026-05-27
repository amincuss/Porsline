using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
using PorslineClone.Application.ContractTemplates;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

public class ContractDocumentTemplateService(
    AppDbContext db,
    ContractTemplateFileStorageService templateFiles,
    IContractDocumentGenerator generator,
    IDocxToPdfConverter pdfConverter)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public async Task<IReadOnlyList<ContractDocumentTemplateListItemDto>> ListAsync(CancellationToken ct)
    {
        var items = await db.ContractDocumentTemplates
            .AsNoTracking()
            .OrderByDescending(x => x.UpdatedAtUtc ?? x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Description,
                x.IsActive,
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.ActiveVersionId,
                FieldCount = x.ActiveVersionId != null
                    ? x.Fields.Count(f => f.VersionId == x.ActiveVersionId)
                    : 0,
                ActiveVersionNumber = x.ActiveVersion != null ? (int?)x.ActiveVersion.VersionNumber : null
            })
            .ToListAsync(ct);

        return items.Select(x => new ContractDocumentTemplateListItemDto(
            x.Id,
            x.Name,
            x.Description,
            x.IsActive,
            x.ActiveVersionNumber,
            x.FieldCount,
            x.CreatedAtUtc,
            x.UpdatedAtUtc)).ToList();
    }

    public async Task<IReadOnlyList<ContractDocumentTemplateActiveOptionDto>> ListActiveForContractCreateAsync(CancellationToken ct)
    {
        var templates = await db.ContractDocumentTemplates
            .AsNoTracking()
            .Where(x => x.IsActive)
            .Include(x => x.Versions.OrderByDescending(v => v.VersionNumber))
                .ThenInclude(v => v.Fields.OrderBy(f => f.SortOrder))
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

        return templates
            .Where(t => t.Versions.Count > 0)
            .Select(t => new ContractDocumentTemplateActiveOptionDto(
                t.Id,
                t.Name,
                t.Versions.Select(v => new ContractDocumentTemplateVersionPickDto(
                    v.Id,
                    v.VersionNumber,
                    v.FileName,
                    v.Id == t.ActiveVersionId,
                    v.Fields.OrderBy(f => f.SortOrder).Select(MapField).ToList())).ToList()))
            .ToList();
    }

    public async Task<ContractDocumentTemplateDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await db.ContractDocumentTemplates
            .AsNoTracking()
            .Include(x => x.Versions.OrderByDescending(v => v.VersionNumber))
                .ThenInclude(v => v.Fields.OrderBy(f => f.SortOrder))
            .Include(x => x.ActiveVersion)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (t is null)
            return null;

        var activeVersion = t.ActiveVersion;
        var placeholders = activeVersion is not null
            ? DeserializePlaceholders(activeVersion.DetectedPlaceholdersJson)
            : [];

        var activeFields = activeVersion is null
            ? []
            : t.Versions
                .Where(v => v.Id == activeVersion.Id)
                .SelectMany(v => v.Fields.OrderBy(f => f.SortOrder))
                .Select(MapField)
                .ToList();

        return new ContractDocumentTemplateDetailDto(
            t.Id,
            t.Name,
            t.Description,
            t.IsActive,
            t.ActiveVersionId,
            activeVersion?.VersionNumber,
            placeholders,
            activeFields,
            t.Versions.Select(v => MapVersionDto(v, t.ActiveVersionId)).ToList());
    }

    public async Task<ContractDocumentTemplateDetailDto> CreateAsync(
        UpsertContractTemplateRequest req,
        Guid userId,
        CancellationToken ct)
    {
        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("نام قالب الزامی است");

        if (await db.ContractDocumentTemplates.AnyAsync(x => x.Name == name, ct))
            throw new InvalidOperationException("قالبی با این نام وجود دارد");

        var entity = new ContractDocumentTemplate
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim(),
            IsActive = req.IsActive ?? true,
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ContractDocumentTemplates.Add(entity);
        await db.SaveChangesAsync(ct);
        return (await GetAsync(entity.Id, ct))!;
    }

    public async Task<ContractDocumentTemplateDetailDto?> UpdateAsync(
        Guid id,
        UpsertContractTemplateRequest req,
        CancellationToken ct)
    {
        var entity = await db.ContractDocumentTemplates.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (entity is null)
            return null;

        var name = (req.Name ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("نام قالب الزامی است");

        if (await db.ContractDocumentTemplates.AnyAsync(x => x.Name == name && x.Id != id, ct))
            throw new InvalidOperationException("قالبی با این نام وجود دارد");

        entity.Name = name;
        entity.Description = string.IsNullOrWhiteSpace(req.Description) ? null : req.Description.Trim();
        if (req.IsActive.HasValue)
            entity.IsActive = req.IsActive.Value;
        entity.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<ContractDocumentTemplateDetailDto?> UploadVersionAsync(
        Guid templateId,
        IFormFile file,
        string? changeNote,
        Guid userId,
        CancellationToken ct)
    {
        var template = await db.ContractDocumentTemplates
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == templateId, ct);
        if (template is null)
            return null;

        ValidateDocx(file);

        var nextVersion = template.Versions.Count == 0 ? 1 : template.Versions.Max(v => v.VersionNumber) + 1;
        var stored = await templateFiles.SaveVersionAsync(templateId, nextVersion, file, ct);
        var fullPath = templateFiles.ResolveFullPath(stored.relativePath);
        var placeholders = generator.ScanPlaceholders(fullPath);

        var version = new ContractDocumentTemplateVersion
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            VersionNumber = nextVersion,
            FilePath = stored.relativePath,
            FileName = stored.originalFileName,
            DetectedPlaceholdersJson = JsonSerializer.Serialize(placeholders, JsonOpts),
            ChangeNote = string.IsNullOrWhiteSpace(changeNote) ? null : changeNote.Trim(),
            CreatedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };
        db.ContractDocumentTemplateVersions.Add(version);
        if (nextVersion == 1)
            template.ActiveVersionId = version.Id;
        template.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        await SyncFieldsFromPlaceholdersAsync(templateId, version.Id, placeholders, removeMissing: false, ct);
        return await GetAsync(templateId, ct);
    }

    public async Task<ContractDocumentTemplateDetailDto?> PublishVersionAsync(Guid templateId, Guid versionId, CancellationToken ct)
    {
        var template = await db.ContractDocumentTemplates.FirstOrDefaultAsync(x => x.Id == templateId, ct);
        if (template is null)
            return null;

        var version = await db.ContractDocumentTemplateVersions
            .FirstOrDefaultAsync(x => x.Id == versionId && x.TemplateId == templateId, ct);
        if (version is null)
            throw new InvalidOperationException("نسخه یافت نشد");

        template.ActiveVersionId = versionId;
        template.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(templateId, ct);
    }

    public async Task<ContractDocumentTemplateDetailDto?> SaveFieldsAsync(
        Guid templateId,
        SaveContractTemplateFieldsRequest req,
        CancellationToken ct)
    {
        var template = await db.ContractDocumentTemplates
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == templateId, ct);
        if (template is null)
            return null;
        if (template.ActiveVersionId is null)
            throw new InvalidOperationException("نسخه پیش‌فرض تعریف نشده است");

        return await SaveVersionFieldsAsync(templateId, template.ActiveVersionId.Value, req, ct);
    }

    public async Task<ContractDocumentTemplateDetailDto?> SaveVersionFieldsAsync(
        Guid templateId,
        Guid versionId,
        SaveContractTemplateFieldsRequest req,
        CancellationToken ct)
    {
        var version = await db.ContractDocumentTemplateVersions
            .Include(v => v.Fields)
            .FirstOrDefaultAsync(x => x.Id == versionId && x.TemplateId == templateId, ct);
        if (version is null)
            return null;

        var placeholders = DeserializePlaceholders(version.DetectedPlaceholdersJson)
            .Select(NormalizeKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        db.ContractDocumentTemplateFields.RemoveRange(version.Fields);
        var order = 0;
        foreach (var f in req.Fields.OrderBy(x => x.SortOrder))
        {
            var key = NormalizeKey(f.Key);
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (placeholders.Count > 0 && !placeholders.Contains(key))
                throw new InvalidOperationException($"فیلد «{key}» در placeholderهای این نسخه Word وجود ندارد");

            var fieldType = ParseFieldType(f.FieldType);
            db.ContractDocumentTemplateFields.Add(new ContractDocumentTemplateField
            {
                Id = Guid.NewGuid(),
                TemplateId = templateId,
                VersionId = versionId,
                Key = key,
                Label = string.IsNullOrWhiteSpace(f.Label) ? key : f.Label.Trim(),
                FieldType = fieldType,
                IsRequired = ContractTemplateSystemFields.IsSystemFieldType(fieldType) ? false : f.IsRequired,
                SortOrder = order++,
                DefaultValue = string.IsNullOrWhiteSpace(f.DefaultValue) ? null : f.DefaultValue.Trim(),
                OptionsJson = string.IsNullOrWhiteSpace(f.OptionsJson) ? null : f.OptionsJson.Trim()
            });
        }

        var template = await db.ContractDocumentTemplates.FirstAsync(x => x.Id == templateId, ct);
        template.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return await GetAsync(templateId, ct);
    }

    public async Task<(Stream stream, string fileName, string contentType, bool pdfFallbackToDocx)> GeneratePreviewAsync(
        Guid templateId,
        IReadOnlyDictionary<string, string> fieldValues,
        bool exportPdf,
        CancellationToken ct)
    {
        var (fullPath, fileName) = await ResolveActiveDocxAsync(templateId, ct);
        var tempDocx = await generator.GenerateDocxAsync(fullPath, NormalizeFieldValues(fieldValues), ct);

        if (exportPdf)
        {
            var tempPdf = pdfConverter.TryConvert(tempDocx);
            if (tempPdf is not null)
            {
                TryDeleteFile(tempDocx);
                var pdfName = $"preview-{Path.ChangeExtension(fileName, ".pdf")}";
                var stream = new FileStream(tempPdf, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
                return (stream, pdfName, "application/pdf", false);
            }

            var docxStream = new FileStream(tempDocx, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
            return (docxStream, $"preview-{fileName}", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", true);
        }

        var outStream = new FileStream(tempDocx, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.DeleteOnClose);
        return (outStream, $"preview-{fileName}", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", false);
    }

    public async Task<(string tempDocxPath, string fileName, Guid versionId)> GenerateForContractAsync(
        Guid templateId,
        Guid? versionId,
        IReadOnlyDictionary<string, string> fieldValues,
        string? contractNumber = null,
        CancellationToken ct = default)
    {
        var template = await db.ContractDocumentTemplates
            .AsNoTracking()
            .Include(x => x.ActiveVersion)
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == templateId && x.IsActive, ct)
            ?? throw new InvalidOperationException("قالب فعال یافت نشد");

        ContractDocumentTemplateVersion? version;
        if (versionId is { } vid && vid != Guid.Empty)
        {
            version = template.Versions.FirstOrDefault(x => x.Id == vid)
                ?? throw new InvalidOperationException("نسخه انتخاب‌شده برای این قالب یافت نشد");
        }
        else
        {
            version = template.ActiveVersion
                ?? throw new InvalidOperationException("نسخه قالب را انتخاب کنید");
        }

        var versionFields = await db.ContractDocumentTemplateFields
            .AsNoTracking()
            .Where(f => f.VersionId == version.Id)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);

        var normalizedValues = ContractTemplateSystemFields.MergeContractNumber(versionFields, fieldValues, contractNumber ?? "");
        ValidateRequiredFields(versionFields, normalizedValues);

        var fullPath = templateFiles.ResolveFullPath(version.FilePath);
        var tempPath = await generator.GenerateDocxAsync(fullPath, normalizedValues, ct);
        return (tempPath, version.FileName, version.Id);
    }

    private async Task<(string fullPath, string fileName)> ResolveActiveDocxAsync(Guid templateId, CancellationToken ct)
    {
        var template = await db.ContractDocumentTemplates
            .AsNoTracking()
            .Include(x => x.ActiveVersion)
            .FirstOrDefaultAsync(x => x.Id == templateId, ct)
            ?? throw new InvalidOperationException("قالب یافت نشد");

        if (template.ActiveVersion is null)
            throw new InvalidOperationException("نسخه فعال قالب تعریف نشده است");

        return (templateFiles.ResolveFullPath(template.ActiveVersion.FilePath), template.ActiveVersion.FileName);
    }

    private async Task SyncFieldsFromPlaceholdersAsync(
        Guid templateId,
        Guid versionId,
        IReadOnlyList<string> placeholders,
        bool removeMissing,
        CancellationToken ct)
    {
        var placeholderKeySet = placeholders
            .Select(NormalizeKey)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var existing = await db.ContractDocumentTemplateFields
            .Where(x => x.VersionId == versionId)
            .ToListAsync(ct);

        if (removeMissing)
        {
            var toRemove = existing
                .Where(f => !placeholderKeySet.Contains(NormalizeKey(f.Key)))
                .ToList();
            if (toRemove.Count > 0)
                db.ContractDocumentTemplateFields.RemoveRange(toRemove);
            existing = existing.Except(toRemove).ToList();
        }

        var existingKeys = existing.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var order = existing.Count > 0 ? existing.Max(x => x.SortOrder) + 1 : 0;

        foreach (var key in placeholderKeySet)
        {
            if (existingKeys.Contains(key))
                continue;

            var isContractNumber = ContractTemplateSystemFields.IsContractNumberKey(key);
            var isImage = ContractTemplateSystemFields.IsImageKey(key);
            var isDate = ContractTemplateSystemFields.IsDateKey(key);
            db.ContractDocumentTemplateFields.Add(new ContractDocumentTemplateField
            {
                Id = Guid.NewGuid(),
                TemplateId = templateId,
                VersionId = versionId,
                Key = key,
                Label = isContractNumber ? "شماره قرارداد" : isImage ? "تصویر" : isDate ? "تاریخ" : key.Replace('_', ' '),
                FieldType = isContractNumber
                    ? ContractTemplateFieldType.ContractNumber
                    : isImage
                        ? ContractTemplateFieldType.Image
                        : isDate
                            ? ContractTemplateFieldType.Date
                            : ContractTemplateFieldType.Text,
                IsRequired = !isContractNumber && !isImage,
                SortOrder = order++
            });
            existingKeys.Add(key);
        }

        await db.SaveChangesAsync(ct);
    }

    private ContractDocumentTemplateVersionDto MapVersionDto(
        ContractDocumentTemplateVersion v,
        Guid? activeVersionId)
    {
        long? fileSize = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(v.FilePath))
            {
                var full = templateFiles.ResolveFullPath(v.FilePath);
                if (File.Exists(full))
                    fileSize = new FileInfo(full).Length;
            }
        }
        catch
        {
            /* ignore */
        }

        return new ContractDocumentTemplateVersionDto(
            v.Id,
            v.VersionNumber,
            v.FileName,
            DeserializePlaceholders(v.DetectedPlaceholdersJson),
            v.ChangeNote,
            v.CreatedAtUtc,
            v.Id == activeVersionId,
            fileSize,
            v.Fields.OrderBy(f => f.SortOrder).Select(MapField).ToList());
    }

    private static Dictionary<string, string> NormalizeFieldValues(IReadOnlyDictionary<string, string> fieldValues)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in fieldValues)
        {
            var norm = NormalizeKey(kv.Key);
            if (string.IsNullOrWhiteSpace(norm))
                continue;
            result[norm] = ContractDocumentGeneratorService.UnwrapStoredFieldValue(kv.Value);
        }

        return result;
    }

    private static void ValidateRequiredFields(
        IEnumerable<ContractDocumentTemplateField> fields,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var f in fields.Where(x => x.IsRequired && !ContractTemplateSystemFields.IsSystemFieldType(x.FieldType)))
        {
            var key = NormalizeKey(f.Key);
            if (string.IsNullOrWhiteSpace(key) ||
                !values.TryGetValue(key, out var v))
                throw new InvalidOperationException($"فیلد «{f.Label}» الزامی است");

            if (f.FieldType == ContractTemplateFieldType.Image)
            {
                if (!ContractTemplateImageValue.HasImageContent(v))
                    throw new InvalidOperationException($"تصویر «{f.Label}» الزامی است");
                continue;
            }

            if (string.IsNullOrWhiteSpace(v))
                throw new InvalidOperationException($"فیلد «{f.Label}» الزامی است");
        }
    }

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

    private static string NormalizeKey(string key)
        => new string((key ?? "").Trim().Where(ch => char.IsLetterOrDigit(ch) || ch == '_').ToArray()).ToLowerInvariant();

    private static ContractTemplateFieldType ParseFieldType(string? type) => (type ?? "text").ToLowerInvariant() switch
    {
        "textarea" => ContractTemplateFieldType.TextArea,
        "number" => ContractTemplateFieldType.Number,
        "date" => ContractTemplateFieldType.Date,
        "phone" => ContractTemplateFieldType.Phone,
        "nationalid" => ContractTemplateFieldType.NationalId,
        "signature" => ContractTemplateFieldType.Signature,
        "contractnumber" => ContractTemplateFieldType.ContractNumber,
        "image" => ContractTemplateFieldType.Image,
        _ => ContractTemplateFieldType.Text
    };

    private static ContractTemplateFieldDto MapField(ContractDocumentTemplateField f) => new(
        f.Id,
        f.Key,
        f.Label,
        f.FieldType.ToString().ToLowerInvariant(),
        f.IsRequired,
        f.SortOrder,
        f.DefaultValue,
        f.OptionsJson);

    public async Task<ContractDocumentTemplateDetailDto?> ReplaceVersionFileAsync(
        Guid templateId,
        Guid versionId,
        IFormFile file,
        CancellationToken ct)
    {
        var version = await db.ContractDocumentTemplateVersions
            .FirstOrDefaultAsync(x => x.Id == versionId && x.TemplateId == templateId, ct);
        if (version is null)
            return null;

        ValidateDocx(file);

        var fullPath = templateFiles.ResolveFullPath(version.FilePath);
        await using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

        IReadOnlyList<string> placeholders;
        try
        {
            placeholders = generator.ScanPlaceholders(fullPath);
        }
        catch (Exception)
        {
            throw new InvalidOperationException("فایل Word ذخیره‌شده نامعتبر است. دوباره از فایل اصلی Word آپلود کنید.");
        }
        version.DetectedPlaceholdersJson = JsonSerializer.Serialize(placeholders, JsonOpts);
        var template = await db.ContractDocumentTemplates.FirstAsync(x => x.Id == templateId, ct);
        template.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await SyncFieldsFromPlaceholdersAsync(templateId, versionId, placeholders, removeMissing: true, ct);
        return await GetAsync(templateId, ct);
    }

    public async Task<ContractDocumentTemplateDetailDto?> InsertPlaceholderAsync(
        Guid templateId,
        Guid versionId,
        string key,
        int paragraphIndex,
        CancellationToken ct)
    {
        var version = await db.ContractDocumentTemplateVersions
            .FirstOrDefaultAsync(x => x.Id == versionId && x.TemplateId == templateId, ct);
        if (version is null)
            return null;

        var fullPath = templateFiles.ResolveFullPath(version.FilePath);
        try
        {
            generator.InsertPlaceholder(fullPath, key, paragraphIndex);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException(ex.Message);
        }

        var placeholders = generator.ScanPlaceholders(fullPath);
        version.DetectedPlaceholdersJson = JsonSerializer.Serialize(placeholders, JsonOpts);
        version.Template = await db.ContractDocumentTemplates.FirstAsync(x => x.Id == templateId, ct);
        version.Template.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await SyncFieldsFromPlaceholdersAsync(templateId, versionId, placeholders, removeMissing: true, ct);
        return await GetAsync(templateId, ct);
    }

    public async Task<(string fullPath, string fileName)?> GetVersionFileAsync(
        Guid templateId,
        Guid versionId,
        CancellationToken ct)
    {
        var version = await db.ContractDocumentTemplateVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == versionId && x.TemplateId == templateId, ct);
        if (version is null)
            return null;

        var fullPath = templateFiles.ResolveFullPath(version.FilePath);
        if (!File.Exists(fullPath))
            return null;

        return (fullPath, version.FileName);
    }

    /// <summary>باز کردن فایل نسخه برای دانلود یا پیش‌نمایش PDF (LibreOffice).</summary>
    public async Task<(Stream Stream, string FileName, string ContentType)?> OpenVersionFileAsync(
        Guid templateId,
        Guid versionId,
        bool asPdf,
        CancellationToken ct)
    {
        var file = await GetVersionFileAsync(templateId, versionId, ct);
        if (file is null)
            return null;

        var (fullPath, fileName) = file.Value;
        if (!asPdf)
        {
            var docxStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (
                docxStream,
                fileName,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
        }

        if (fullPath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var pdfStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return (pdfStream, fileName, "application/pdf");
        }

        if (!fullPath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فرمت فایل برای پیش‌نمایش PDF پشتیبانی نمی‌شود.");

        if (!pdfConverter.IsAvailable)
        {
            throw new InvalidOperationException(
                "تبدیل Word به PDF فعال نیست. LibreOffice را روی سرور API نصب و سرویس را ری‌استارت کنید.");
        }

        var generatedPdf = pdfConverter.TryConvert(fullPath);
        if (generatedPdf is null)
            throw new InvalidOperationException("تبدیل فایل Word به PDF ناموفق بود.");

        var stream = new FileStream(
            generatedPdf,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            4096,
            FileOptions.DeleteOnClose);
        var pdfName = Path.ChangeExtension(fileName, ".pdf");
        return (stream, pdfName, "application/pdf");
    }

    public async Task<ContractDocumentTemplateVersion?> GetVersionEntityAsync(
        Guid templateId,
        Guid versionId,
        CancellationToken ct)
        => await db.ContractDocumentTemplateVersions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == versionId && x.TemplateId == templateId, ct);

    public async Task RefreshVersionAfterExternalEditAsync(Guid templateId, Guid versionId, CancellationToken ct)
    {
        var version = await db.ContractDocumentTemplateVersions
            .FirstOrDefaultAsync(x => x.Id == versionId && x.TemplateId == templateId, ct);
        if (version is null)
            return;

        var fullPath = templateFiles.ResolveFullPath(version.FilePath);
        var placeholders = generator.ScanPlaceholders(fullPath);
        version.DetectedPlaceholdersJson = JsonSerializer.Serialize(placeholders, JsonOpts);
        var template = await db.ContractDocumentTemplates.FirstAsync(x => x.Id == templateId, ct);
        template.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        await SyncFieldsFromPlaceholdersAsync(templateId, versionId, placeholders, removeMissing: true, ct);
    }

    public bool IsPdfConversionAvailable => pdfConverter.IsAvailable;

    public async Task<bool> DeleteVersionAsync(Guid templateId, Guid versionId, CancellationToken ct)
    {
        var template = await db.ContractDocumentTemplates
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(x => x.Id == templateId, ct);
        if (template is null)
            return false;

        var version = template.Versions.FirstOrDefault(v => v.Id == versionId);
        if (version is null)
            return false;

        var usedByContracts = await db.Contracts
            .AnyAsync(c => c.ContractDocumentTemplateVersionId == versionId, ct);
        if (usedByContracts)
            throw new InvalidOperationException("این نسخه در قراردادهای صادرشده استفاده شده و قابل حذف نیست.");

        if (template.ActiveVersionId == versionId)
        {
            var replacement = template.Versions
                .Where(v => v.Id != versionId)
                .OrderByDescending(v => v.VersionNumber)
                .FirstOrDefault();
            template.ActiveVersionId = replacement?.Id;
            template.ActiveVersion = replacement;
            template.UpdatedAtUtc = DateTime.UtcNow;
            // شکستن FK دایره‌ای ActiveVersionId ↔ TemplateId قبل از حذف نسخه
            await db.SaveChangesAsync(ct);
        }

        TryDeleteFile(templateFiles.ResolveFullPath(version.FilePath));
        db.ContractDocumentTemplateVersions.Remove(version);
        template.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return true;
    }

    public async Task<bool> DeleteTemplateAsync(Guid id, CancellationToken ct)
    {
        var template = await db.ContractDocumentTemplates
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (template is null)
            return false;

        var usedCount = await db.Contracts.CountAsync(c => c.ContractDocumentTemplateId == id, ct);
        if (usedCount > 0)
            throw new InvalidOperationException(
                $"این قالب در {usedCount} قرارداد استفاده شده و قابل حذف نیست.");

        foreach (var v in template.Versions)
            TryDeleteFile(templateFiles.ResolveFullPath(v.FilePath));

        // شکستن وابستگی دایره‌ای Template.ActiveVersionId ↔ Version.TemplateId
        template.ActiveVersionId = null;
        template.ActiveVersion = null;
        template.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        db.ContractDocumentTemplates.Remove(template);
        await db.SaveChangesAsync(ct);
        templateFiles.TryDeleteTemplateStorage(id);
        return true;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { /* ignore */ }
    }

    private static IReadOnlyList<string> DeserializePlaceholders(string? json)
    {
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json ?? "[]", JsonOpts) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
