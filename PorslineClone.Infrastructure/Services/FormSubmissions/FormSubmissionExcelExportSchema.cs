using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;
using PorslineClone.Infrastructure.Persistence;

namespace PorslineClone.Infrastructure.Services.FormSubmissions;

/// <summary>
/// اسکیمای استاندارد خروجی Excel — یک ستون = یک مقدار مشخص، تطبیق فقط با FieldId.
/// فیلد تکرارشونده: ستون جدا برای هر (ردیف × فیلد تو در تو).
/// </summary>
internal static class FormSubmissionExcelExportSchema
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private static readonly HashSet<FieldType> SkippedFieldTypes =
    [
        FieldType.Heading,
        FieldType.Paragraph,
        FieldType.WizardStepHeader,
        FieldType.Guide,
        FieldType.FixedConstant,
    ];

    internal enum ColumnKind
    {
        Meta,
        SimpleField,
        RepeatableCell,
    }

    internal sealed record ColumnDef(
        string Key,
        string Header,
        ColumnKind Kind,
        Guid? FieldId,
        Guid? ParentFieldId,
        string? SourceLabel,
        int RepeatableRow,
        string? NestedFieldId,
        FieldType FieldType,
        bool IsFile);

    internal sealed record FormExportContext(
        Guid FormId,
        string FormTitle,
        IReadOnlyList<FormField> Fields,
        IReadOnlyDictionary<Guid, FormField> FieldById,
        IReadOnlyDictionary<string, FormField> FieldByLabel,
        IReadOnlyDictionary<Guid, int> MaxRepeatableRows);

    internal static async Task<FormExportContext> LoadContextAsync(
        AppDbContext db,
        Guid formId,
        IEnumerable<string?> submissionFieldsJson,
        CancellationToken ct)
    {
        var fields = await db.FormFields.AsNoTracking()
            .Where(f => f.FormId == formId)
            .OrderBy(f => f.SortOrder)
            .ToListAsync(ct);

        var formTitle = await db.Forms.AsNoTracking()
            .Where(f => f.Id == formId)
            .Select(f => f.Title)
            .FirstOrDefaultAsync(ct) ?? "فرم";

        var repeatableParents = fields.Where(f => f.FieldType == FieldType.Repeatable).ToList();
        var maxRows = ComputeMaxRepeatableRows(submissionFieldsJson, repeatableParents, fields);

        return new FormExportContext(
            formId,
            formTitle,
            fields,
            fields.ToDictionary(f => f.Id),
            fields
                .Where(f => !string.IsNullOrWhiteSpace(f.Label))
                .GroupBy(f => f.Label.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase),
            maxRows);
    }

    internal static IReadOnlyList<ColumnDef> BuildAllColumns(FormExportContext ctx)
    {
        var columns = new List<ColumnDef>();
        columns.AddRange(BuildMetaColumns());

        foreach (var field in ctx.Fields)
        {
            if (SkippedFieldTypes.Contains(field.FieldType)) continue;

            if (field.FieldType == FieldType.Repeatable)
            {
                columns.AddRange(BuildRepeatableColumns(field, ctx.MaxRepeatableRows));
                continue;
            }

            columns.Add(BuildSimpleColumn(field));
        }

        return columns;
    }

    internal static IReadOnlyList<ColumnDef> ResolveSelectedColumns(
        FormExportContext ctx,
        IReadOnlyList<string> selectedKeys)
    {
        var all = BuildAllColumns(ctx);
        if (selectedKeys.Count == 0) return all;

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in selectedKeys)
        {
            foreach (var key in ExpandLegacyKey(raw, ctx, all))
                selected.Add(key);
        }

        return all.Where(c => selected.Contains(c.Key)).ToList();
    }

    internal static int CountFilled(ColumnDef column, string? fieldsJson)
    {
        var index = SubmissionValueIndex.Parse(fieldsJson);
        return string.IsNullOrWhiteSpace(ResolveCellValue(null, index, column)) ? 0 : 1;
    }

    internal static string ResolveCellValue(
        FormSubmission? submission,
        SubmissionValueIndex index,
        ColumnDef column)
    {
        return column.Kind switch
        {
            ColumnKind.Meta when submission is not null => ResolveMeta(submission, column.Key),
            ColumnKind.SimpleField => FormatSimpleValue(
                index.GetRawValue(column.FieldId!.Value, column.SourceLabel),
                column.FieldType),
            ColumnKind.RepeatableCell => FormatRepeatableCell(
                index.GetRawValue(column.ParentFieldId!.Value, column.SourceLabel),
                column.RepeatableRow,
                column.NestedFieldId!),
            _ => "",
        };
    }

    private static IEnumerable<string> ExpandLegacyKey(
        string raw,
        FormExportContext ctx,
        IReadOnlyList<ColumnDef> allColumns)
    {
        if (raw.StartsWith("meta:", StringComparison.OrdinalIgnoreCase)
            || raw.StartsWith(ColumnKeys.FieldPrefix, StringComparison.OrdinalIgnoreCase))
        {
            yield return raw;
            yield break;
        }

        if (FormSubmissionExcelFieldKeys.TryParseLegacyRepeatableKey(raw, out var parentId, out var nestedId))
        {
            foreach (var col in allColumns.Where(c =>
                         c.Kind == ColumnKind.RepeatableCell
                         && c.ParentFieldId == parentId
                         && string.Equals(c.NestedFieldId, nestedId, StringComparison.OrdinalIgnoreCase)))
                yield return col.Key;
            yield break;
        }

        if (ctx.FieldByLabel.TryGetValue(raw.Trim(), out var byLabel))
        {
            if (byLabel.FieldType == FieldType.Repeatable)
            {
                foreach (var col in allColumns.Where(c =>
                             c.Kind == ColumnKind.RepeatableCell && c.ParentFieldId == byLabel.Id))
                    yield return col.Key;
            }
            else
            {
                yield return ColumnKeys.SimpleField(byLabel.Id);
            }

            yield break;
        }

        yield return raw;
    }

    private static List<ColumnDef> BuildMetaColumns() =>
        FormSubmissionExcelFieldKeys.MetaColumns
            .Select(m => new ColumnDef(
                m.Key,
                m.Label,
                ColumnKind.Meta,
                null,
                null,
                null,
                0,
                null,
                FieldType.ShortText,
                false))
            .ToList();

    private static ColumnDef BuildSimpleColumn(FormField field) =>
        new(
            ColumnKeys.SimpleField(field.Id),
            field.Label.Trim(),
            ColumnKind.SimpleField,
            field.Id,
            null,
            field.Label.Trim(),
            0,
            null,
            field.FieldType,
            field.FieldType is FieldType.FileUpload or FieldType.ImageUpload or FieldType.PersonalPhoto);

    private static IEnumerable<ColumnDef> BuildRepeatableColumns(
        FormField parent,
        IReadOnlyDictionary<Guid, int> maxRowsByParent)
    {
        var nested = DeserializeNested(parent.NestedFieldsJson);
        if (nested.Count == 0) yield break;

        var parentLabel = parent.Label.Trim();
        var rowCount = maxRowsByParent.TryGetValue(parent.Id, out var max) ? Math.Max(max, 0) : 0;
        if (rowCount <= 0) rowCount = 1;

        for (var row = 1; row <= rowCount; row++)
        {
            foreach (var nf in nested)
            {
                var nestedId = nf.Id?.Trim() ?? "";
                if (string.IsNullOrEmpty(nestedId)) continue;
                var nestedLabel = string.IsNullOrWhiteSpace(nf.Label) ? "فیلد" : nf.Label.Trim();
                var nestedType = Enum.IsDefined(typeof(FieldType), nf.FieldType)
                    ? (FieldType)nf.FieldType
                    : FieldType.ShortText;

                yield return new ColumnDef(
                    ColumnKeys.RepeatableCell(parent.Id, row, nestedId),
                    $"{parentLabel} [{row}] — {nestedLabel}",
                    ColumnKind.RepeatableCell,
                    null,
                    parent.Id,
                    parentLabel,
                    row,
                    nestedId,
                    nestedType,
                    nestedType is FieldType.FileUpload or FieldType.ImageUpload);
            }
        }
    }

    private static Dictionary<Guid, int> ComputeMaxRepeatableRows(
        IEnumerable<string?> submissionFieldsJson,
        IReadOnlyList<FormField> repeatableParents,
        IReadOnlyList<FormField> allFields)
    {
        var max = repeatableParents.ToDictionary(f => f.Id, _ => 0);
        if (repeatableParents.Count == 0) return max;

        foreach (var json in submissionFieldsJson)
        {
            var index = SubmissionValueIndex.Parse(json);
            foreach (var parent in repeatableParents)
            {
                var raw = index.GetRawValue(parent.Id, parent.Label);
                var count = RepeatableJson.CountRows(raw);
                if (count > max[parent.Id]) max[parent.Id] = count;
            }
        }

        return max;
    }

    private static string ResolveMeta(FormSubmission submission, string key) => key switch
    {
        FormSubmissionExcelFieldKeys.SubmitterName => submission.SubmitterName ?? "",
        FormSubmissionExcelFieldKeys.SubmitterMobile => submission.SubmitterEmail ?? "",
        FormSubmissionExcelFieldKeys.TrackingCode => submission.TrackingCode ?? "",
        FormSubmissionExcelFieldKeys.SubmittedAt =>
            SmsDateTimeFormatter.FormatUtcTehran(submission.SubmittedAtUtc).Date + " "
            + SmsDateTimeFormatter.FormatUtcTehran(submission.SubmittedAtUtc).Time,
        FormSubmissionExcelFieldKeys.ApprovalStatus => FormatStatus(submission.Status),
        _ => "",
    };

    private static string FormatSimpleValue(string? raw, FieldType fieldType)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";
        var v = raw.Trim();
        if (FormSubmissionUploadHelper.IsUploadPath(v))
            return Path.GetFileName(FormSubmissionUploadHelper.NormalizeRelativePath(v) ?? v);
        if (fieldType == FieldType.Checkbox)
            return v is "true" or "1" or "True" or "بله" ? "بله" : "خیر";
        return v;
    }

    private static string FormatRepeatableCell(string? parentJson, int rowIndex, string nestedFieldId)
    {
        if (string.IsNullOrWhiteSpace(parentJson)) return "";
        try
        {
            using var doc = JsonDocument.Parse(parentJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "";

            var i = 0;
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                i++;
                if (i != rowIndex) continue;
                if (row.ValueKind != JsonValueKind.Object) return "";
                if (!RepeatableJson.TryGetProperty(row, nestedFieldId, out var cell)) return "";
                return RepeatableJson.ElementToString(cell);
            }
        }
        catch (JsonException) { /* ignore */ }

        return "";
    }

    private static string FormatStatus(FormSubmissionStatus status) => status switch
    {
        FormSubmissionStatus.Pending => "منتظر شروع",
        FormSubmissionStatus.InProgress => "در جریان تأیید",
        FormSubmissionStatus.Approved => "تأیید شده",
        FormSubmissionStatus.Rejected => "رد شده",
        FormSubmissionStatus.Submitted => "ثبت شده",
        _ => status.ToString(),
    };

    private static List<NestedFormFieldDto> DeserializeNested(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? []
            : JsonSerializer.Deserialize<List<NestedFormFieldDto>>(json, JsonOpts) ?? [];

    internal static class ColumnKeys
    {
        public const string FieldPrefix = "field:";

        public static string SimpleField(Guid fieldId) => $"{FieldPrefix}{fieldId:D}";

        public static string RepeatableCell(Guid parentFieldId, int row, string nestedFieldId) =>
            $"{FieldPrefix}{parentFieldId:D}:r{row}:n{nestedFieldId}";
    }

    internal sealed class SubmissionValueIndex
    {
        private readonly Dictionary<Guid, string> _byFieldId = new();
        private readonly Dictionary<string, string> _byLabel = new(StringComparer.OrdinalIgnoreCase);

        public static SubmissionValueIndex Parse(string? fieldsJson)
        {
            var index = new SubmissionValueIndex();
            if (string.IsNullOrWhiteSpace(fieldsJson)) return index;

            List<FormFieldValueDto> values;
            try
            {
                values = JsonSerializer.Deserialize<List<FormFieldValueDto>>(fieldsJson, JsonOpts) ?? [];
            }
            catch (JsonException)
            {
                return index;
            }

            foreach (var v in values)
            {
                if (v.FieldId is Guid fid && fid != Guid.Empty)
                    index._byFieldId[fid] = v.Value ?? "";
                if (!string.IsNullOrWhiteSpace(v.Label))
                    index._byLabel[v.Label.Trim()] = v.Value ?? "";
            }

            return index;
        }

        public string? GetRawValue(Guid fieldId, string? labelFallback = null)
        {
            if (_byFieldId.TryGetValue(fieldId, out var byId)) return byId;
            if (!string.IsNullOrWhiteSpace(labelFallback)
                && _byLabel.TryGetValue(labelFallback.Trim(), out var byLabel))
                return byLabel;
            return null;
        }
    }

    internal static class RepeatableJson
    {
        public static int CountRows(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return 0;
            try
            {
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.ValueKind == JsonValueKind.Array
                    ? doc.RootElement.GetArrayLength()
                    : 0;
            }
            catch (JsonException)
            {
                return 0;
            }
        }

        public static bool TryGetProperty(JsonElement row, string propertyName, out JsonElement value)
        {
            value = default;
            if (string.IsNullOrEmpty(propertyName)) return false;
            if (row.TryGetProperty(propertyName, out value)) return true;

            foreach (var prop in row.EnumerateObject())
            {
                if (!string.Equals(prop.Name, propertyName, StringComparison.OrdinalIgnoreCase)) continue;
                value = prop.Value;
                return true;
            }

            return false;
        }

        public static string ElementToString(JsonElement cell) => cell.ValueKind switch
        {
            JsonValueKind.String => cell.GetString() ?? "",
            JsonValueKind.Number => cell.GetRawText(),
            JsonValueKind.True => "بله",
            JsonValueKind.False => "خیر",
            JsonValueKind.Null => "",
            _ => cell.GetRawText(),
        };
    }
}

internal static class FormSubmissionExcelFieldKeys
{
    public const string SubmitterName = "meta:submitterName";
    public const string SubmitterMobile = "meta:submitterMobile";
    public const string TrackingCode = "meta:trackingCode";
    public const string SubmittedAt = "meta:submittedAt";
    public const string ApprovalStatus = "meta:approvalStatus";
    private const string LegacyRepeatablePrefix = "__rf__:";

    public static readonly IReadOnlyList<(string Key, string Label)> MetaColumns =
    [
        (SubmitterName, "نام ثبت‌کننده"),
        (SubmitterMobile, "موبایل"),
        (TrackingCode, "کد پیگیری"),
        (SubmittedAt, "تاریخ ثبت"),
        (ApprovalStatus, "وضعیت تأیید"),
    ];

    public static string LegacyRepeatableKey(Guid parentFieldId, string nestedId) =>
        $"{LegacyRepeatablePrefix}{parentFieldId:D}|{nestedId}";

    public static bool TryParseLegacyRepeatableKey(string key, out Guid parentFieldId, out string nestedId)
    {
        parentFieldId = Guid.Empty;
        nestedId = "";
        if (!key.StartsWith(LegacyRepeatablePrefix, StringComparison.Ordinal)) return false;
        var rest = key[LegacyRepeatablePrefix.Length..];
        var pipe = rest.IndexOf('|');
        if (pipe <= 0) return false;
        if (!Guid.TryParse(rest[..pipe], out parentFieldId)) return false;
        nestedId = rest[(pipe + 1)..];
        return !string.IsNullOrEmpty(nestedId);
    }
}
