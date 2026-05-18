using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Abstractions;
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
                FieldCount = x.Fields.Count,
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
            .Where(x => x.IsActive && x.ActiveVersionId != null)
            .Include(x => x.Fields.OrderBy(f => f.SortOrder))
            .OrderBy(x => x.Name)
            .ToListAsync(ct);

        return templates.Select(t => new ContractDocumentTemplateActiveOptionDto(
            t.Id,
            t.Name,
            t.Fields.Select(MapField).ToList())).ToList();
    }

    public async Task<ContractDocumentTemplateDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var t = await db.ContractDocumentTemplates
            .AsNoTracking()
            .Include(x => x.Fields.OrderBy(f => f.SortOrder))
            .Include(x => x.Versions.OrderByDescending(v => v.VersionNumber))
            .Include(x => x.ActiveVersion)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

        if (t is null)
            return null;

        var activeVersion = t.ActiveVersion;
        var placeholders = activeVersion is not null
            ? DeserializePlaceholders(activeVersion.DetectedPlaceholdersJson)
            : [];

        return new ContractDocumentTemplateDetailDto(
            t.Id,
            t.Name,
            t.Description,
            t.IsActive,
            t.ActiveVersionId,
            activeVersion?.VersionNumber,
            placeholders,
            t.Fields.Select(MapField).ToList(),
            t.Versions.Select(v => new ContractDocumentTemplateVersionDto(
                v.Id,
                v.VersionNumber,
                v.FileName,
                DeserializePlaceholders(v.DetectedPlaceholdersJson),
                v.ChangeNote,
                v.CreatedAtUtc,
                v.Id == t.ActiveVersionId)).ToList());
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

        await SyncFieldsFromPlaceholdersAsync(templateId, placeholders, ct);
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
            .Include(x => x.Fields)
            .FirstOrDefaultAsync(x => x.Id == templateId, ct);
        if (template is null)
            return null;

        db.ContractDocumentTemplateFields.RemoveRange(template.Fields);
        var order = 0;
        foreach (var f in req.Fields.OrderBy(x => x.SortOrder))
        {
            var key = NormalizeKey(f.Key);
            if (string.IsNullOrWhiteSpace(key))
                continue;

            db.ContractDocumentTemplateFields.Add(new ContractDocumentTemplateField
            {
                Id = Guid.NewGuid(),
                TemplateId = templateId,
                Key = key,
                Label = string.IsNullOrWhiteSpace(f.Label) ? key : f.Label.Trim(),
                FieldType = ParseFieldType(f.FieldType),
                IsRequired = f.IsRequired,
                SortOrder = order++,
                DefaultValue = string.IsNullOrWhiteSpace(f.DefaultValue) ? null : f.DefaultValue.Trim(),
                OptionsJson = string.IsNullOrWhiteSpace(f.OptionsJson) ? null : f.OptionsJson.Trim()
            });
        }

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
        var tempDocx = await generator.GenerateDocxAsync(fullPath, fieldValues, ct);

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
        IReadOnlyDictionary<string, string> fieldValues,
        CancellationToken ct)
    {
        var template = await db.ContractDocumentTemplates
            .AsNoTracking()
            .Include(x => x.Fields)
            .Include(x => x.ActiveVersion)
            .FirstOrDefaultAsync(x => x.Id == templateId && x.IsActive, ct)
            ?? throw new InvalidOperationException("قالب فعال یافت نشد");

        if (template.ActiveVersion is null)
            throw new InvalidOperationException("نسخه فعال قالب تعریف نشده است");

        ValidateRequiredFields(template.Fields, fieldValues);

        var fullPath = templateFiles.ResolveFullPath(template.ActiveVersion.FilePath);
        var tempPath = await generator.GenerateDocxAsync(fullPath, fieldValues, ct);
        return (tempPath, template.ActiveVersion.FileName, template.ActiveVersion.Id);
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

    private async Task SyncFieldsFromPlaceholdersAsync(Guid templateId, IReadOnlyList<string> placeholders, CancellationToken ct)
    {
        var existing = await db.ContractDocumentTemplateFields
            .Where(x => x.TemplateId == templateId)
            .ToListAsync(ct);

        var existingKeys = existing.Select(x => x.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var order = existing.Count > 0 ? existing.Max(x => x.SortOrder) + 1 : 0;

        foreach (var ph in placeholders)
        {
            var key = NormalizeKey(ph);
            if (string.IsNullOrWhiteSpace(key) || existingKeys.Contains(key))
                continue;

            db.ContractDocumentTemplateFields.Add(new ContractDocumentTemplateField
            {
                Id = Guid.NewGuid(),
                TemplateId = templateId,
                Key = key,
                Label = key.Replace('_', ' '),
                FieldType = ContractTemplateFieldType.Text,
                IsRequired = true,
                SortOrder = order++
            });
            existingKeys.Add(key);
        }

        await db.SaveChangesAsync(ct);
    }

    private static void ValidateRequiredFields(
        IEnumerable<ContractDocumentTemplateField> fields,
        IReadOnlyDictionary<string, string> values)
    {
        foreach (var f in fields.Where(x => x.IsRequired))
        {
            if (!values.TryGetValue(f.Key, out var v) || string.IsNullOrWhiteSpace(v))
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

    public bool IsPdfConversionAvailable => pdfConverter.IsAvailable;

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
