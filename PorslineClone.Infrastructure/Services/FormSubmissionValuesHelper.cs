using System.Text.Json;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services;

public static class FormSubmissionValuesHelper
{
    private static bool TryGetJsonPropertyIgnoreCase(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.TryGetProperty(name, out value))
            return true;

        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = prop.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool RepeatableRowHasAnyValue(JsonElement row)
    {
        if (row.ValueKind != JsonValueKind.Object)
            return false;

        foreach (var prop in row.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Null)
                continue;
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                if (!string.IsNullOrWhiteSpace(prop.Value.GetString()))
                    return true;
                continue;
            }

            return true;
        }

        return false;
    }

    private static int CountNonEmptyRepeatableRows(JsonElement array)
    {
        var count = 0;
        foreach (var row in array.EnumerateArray())
        {
            if (RepeatableRowHasAnyValue(row))
                count++;
        }

        return count;
    }

    public static Dictionary<string, string> ParseValuesJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new Dictionary<string, string>();
        return new Dictionary<string, string>(raw, StringComparer.OrdinalIgnoreCase);
    }

    public static List<FormFieldValueDto> BuildStoredFieldValues(Form form, IReadOnlyDictionary<string, string> values)
    {
        return form.Fields
            .Select(f => new FormFieldValueDto(
                f.Label,
                PersianDigitHelper.PersianizeForFormStorage(
                    values.TryGetValue(f.Id.ToString(), out var v) ? v : "",
                    f.FieldType),
                f.Id))
            .ToList();
    }

    public static string? ValidateRepeatableFields(Form form, IDictionary<string, string> values)
    {
        foreach (var ff in form.Fields.Where(x => x.FieldType == FieldType.Repeatable))
        {
            values.TryGetValue(ff.Id.ToString(), out var raw);
            raw ??= "";
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (ff.IsRequired)
                    return $"فیلد «{ff.Label}» الزامی است";
                values[ff.Id.ToString()] = "[]";
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                    return $"فرمت فیلد «{ff.Label}» نامعتبر است";

                var nonEmptyRowCount = CountNonEmptyRepeatableRows(doc.RootElement);
                if (ff.IsRequired && nonEmptyRowCount == 0)
                    return $"فیلد «{ff.Label}» الزامی است — حداقل یک ردیف اضافه کنید";

                var nested = string.IsNullOrWhiteSpace(ff.NestedFieldsJson)
                    ? []
                    : JsonSerializer.Deserialize<List<NestedFormFieldDto>>(ff.NestedFieldsJson) ?? [];

                var rowIndex = 0;
                foreach (var row in doc.RootElement.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object) continue;
                    if (!RepeatableRowHasAnyValue(row)) continue;
                    rowIndex++;
                    foreach (var child in nested.Where(c => c.IsRequired))
                    {
                        var childId = child.Id?.Trim() ?? "";
                        if (string.IsNullOrEmpty(childId)) continue;
                        if (!TryGetJsonPropertyIgnoreCase(row, childId, out var cell) || cell.ValueKind == JsonValueKind.Null
                            || (cell.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(cell.GetString())))
                        {
                            return $"فیلد «{child.Label}» در ردیف {rowIndex} از «{ff.Label}» الزامی است";
                        }
                    }
                }
            }
            catch (JsonException)
            {
                return $"فرمت فیلد «{ff.Label}» نامعتبر است";
            }
        }

        return null;
    }
}
