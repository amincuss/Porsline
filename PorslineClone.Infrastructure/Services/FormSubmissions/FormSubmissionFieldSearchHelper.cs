using System.Text.Json;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services.FormSubmissions;

/// <summary>
/// جستجو در مقادیر فیلدهای فرم — شامل فیلدهای تکرارشونده و تو در تو.
/// </summary>
public static class FormSubmissionFieldSearchHelper
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public const string AllFieldsKey = "all";

    public static bool Matches(
        FormSubmission submission,
        string? search,
        string? fieldKey = null)
    {
        if (string.IsNullOrWhiteSpace(search))
            return true;

        var term = search.Trim();
        var key = string.IsNullOrWhiteSpace(fieldKey) ? AllFieldsKey : fieldKey.Trim();

        if (string.Equals(key, AllFieldsKey, StringComparison.OrdinalIgnoreCase))
            return MatchesGeneral(submission, term);

        if (key.StartsWith("meta:", StringComparison.OrdinalIgnoreCase))
            return MatchesMeta(submission, key, term);

        if (TryParseSimpleFieldKey(key, out var fieldId))
        {
            var raw = GetFieldRawValue(submission.FieldsJson, fieldId);
            return ValueMatches(raw, term);
        }

        if (TryParseRepeatableCellKey(key, out var parentId, out var row, out var nestedId))
        {
            var cell = GetRepeatableCellValue(submission.FieldsJson, parentId, row, nestedId);
            return ValueMatches(cell, term);
        }

        if (TryParseLegacyRepeatableKey(key, out parentId, out nestedId))
        {
            return GetAllRepeatableNestedValues(submission.FieldsJson, parentId, nestedId)
                .Any(v => ValueMatches(v, term));
        }

        return MatchesGeneral(submission, term);
    }

    public static bool MatchesGeneral(FormSubmission submission, string term)
    {
        if (string.IsNullOrWhiteSpace(term)) return true;

        var haystacks = new List<string>();
        if (!string.IsNullOrWhiteSpace(submission.SubmitterName)) haystacks.Add(submission.SubmitterName);
        if (!string.IsNullOrWhiteSpace(submission.SubmitterEmail)) haystacks.Add(submission.SubmitterEmail);
        if (!string.IsNullOrWhiteSpace(submission.TrackingCode)) haystacks.Add(submission.TrackingCode);
        if (submission.Form?.Title is { } t && !string.IsNullOrWhiteSpace(t)) haystacks.Add(t);

        haystacks.AddRange(ExtractAllValueTexts(submission.FieldsJson));

        var combined = string.Join(" ", haystacks);
        var digits = NormalizeDigits(term);
        if (digits.Length >= 4)
        {
            var digitHay = string.Concat(haystacks.Select(NormalizeDigits));
            if (digitHay.Contains(digits, StringComparison.Ordinal)) return true;
        }

        return combined.Contains(term, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesMeta(FormSubmission submission, string key, string term)
    {
        var val = key.ToLowerInvariant() switch
        {
            "meta:submittername" => submission.SubmitterName ?? "",
            "meta:submittermobile" => submission.SubmitterEmail ?? "",
            "meta:trackingcode" => submission.TrackingCode ?? "",
            "meta:submittedat" => submission.SubmittedAtUtc.ToString("O"),
            "meta:approvalstatus" => submission.Status.ToString(),
            _ => "",
        };
        return ValueMatches(val, term);
    }

    private static bool ValueMatches(string? raw, string term)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        if (raw.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;

        var digits = NormalizeDigits(term);
        if (digits.Length >= 3 && NormalizeDigits(raw).Contains(digits, StringComparison.Ordinal))
            return true;

        foreach (var nested in ExtractNestedTexts(raw))
        {
            if (nested.Contains(term, StringComparison.OrdinalIgnoreCase)) return true;
            if (digits.Length >= 3 && NormalizeDigits(nested).Contains(digits, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> ExtractAllValueTexts(string? fieldsJson)
    {
        foreach (var field in ParseFields(fieldsJson))
        {
            if (!string.IsNullOrWhiteSpace(field.Label))
                yield return field.Label;
            foreach (var text in ExtractNestedTexts(field.Value))
                yield return text;
        }
    }

    private static IEnumerable<string> ExtractNestedTexts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return [];

        var trimmed = raw.Trim();
        if (!trimmed.StartsWith('[')) return [raw];

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var texts = new List<string>();
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                foreach (var prop in row.EnumerateObject())
                    texts.Add(ElementToString(prop.Value));
            }

            return texts;
        }
        catch (JsonException)
        {
            return [raw];
        }
    }

    private static IEnumerable<string> GetAllRepeatableNestedValues(
        string? fieldsJson,
        Guid parentFieldId,
        string nestedFieldId)
    {
        var raw = GetFieldRawValue(fieldsJson, parentFieldId);
        if (string.IsNullOrWhiteSpace(raw)) return [];

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return [];

            var texts = new List<string>();
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                if (TryGetProperty(row, nestedFieldId, out var cell))
                    texts.Add(ElementToString(cell));
            }

            return texts;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string GetRepeatableCellValue(
        string? fieldsJson,
        Guid parentFieldId,
        int row,
        string nestedFieldId)
    {
        var raw = GetFieldRawValue(fieldsJson, parentFieldId);
        if (string.IsNullOrWhiteSpace(raw)) return "";

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return "";

            var idx = 0;
            foreach (var rowEl in doc.RootElement.EnumerateArray())
            {
                idx++;
                if (idx != row || rowEl.ValueKind != JsonValueKind.Object) continue;
                if (TryGetProperty(rowEl, nestedFieldId, out var cell))
                    return ElementToString(cell);
                return "";
            }
        }
        catch (JsonException)
        {
            return "";
        }

        return "";
    }

    private static string? GetFieldRawValue(string? fieldsJson, Guid fieldId)
    {
        foreach (var field in ParseFields(fieldsJson))
        {
            if (field.FieldId == fieldId)
                return field.Value;
        }

        return null;
    }

    private static List<FormFieldValueDto> ParseFields(string? fieldsJson)
    {
        if (string.IsNullOrWhiteSpace(fieldsJson)) return [];
        try
        {
            return JsonSerializer.Deserialize<List<FormFieldValueDto>>(fieldsJson, JsonOpts) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static bool TryParseSimpleFieldKey(string key, out Guid fieldId)
    {
        fieldId = Guid.Empty;
        const string prefix = "field:";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var rest = key[prefix.Length..];
        var colon = rest.IndexOf(':');
        if (colon >= 0) rest = rest[..colon];
        return Guid.TryParse(rest, out fieldId);
    }

    private static bool TryParseRepeatableCellKey(
        string key,
        out Guid parentFieldId,
        out int row,
        out string nestedFieldId)
    {
        parentFieldId = Guid.Empty;
        row = 0;
        nestedFieldId = "";
        const string prefix = "field:";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;

        var rest = key[prefix.Length..];
        var rIdx = rest.IndexOf(":r", StringComparison.OrdinalIgnoreCase);
        if (rIdx <= 0) return false;
        if (!Guid.TryParse(rest[..rIdx], out parentFieldId)) return false;

        rest = rest[(rIdx + 2)..];
        var nIdx = rest.IndexOf(":n", StringComparison.OrdinalIgnoreCase);
        if (nIdx <= 0) return false;
        if (!int.TryParse(rest[..nIdx], out row) || row < 1) return false;

        nestedFieldId = rest[(nIdx + 2)..];
        return !string.IsNullOrWhiteSpace(nestedFieldId);
    }

    private static bool TryParseLegacyRepeatableKey(string key, out Guid parentFieldId, out string nestedFieldId)
    {
        parentFieldId = Guid.Empty;
        nestedFieldId = "";
        const string legacyPrefix = "__rf__:";
        if (!key.StartsWith(legacyPrefix, StringComparison.Ordinal)) return false;
        var rest = key[legacyPrefix.Length..];
        var pipe = rest.IndexOf('|');
        if (pipe <= 0) return false;
        if (!Guid.TryParse(rest[..pipe], out parentFieldId)) return false;
        nestedFieldId = rest[(pipe + 1)..];
        return !string.IsNullOrEmpty(nestedFieldId);
    }

    private static bool TryGetProperty(JsonElement row, string propertyName, out JsonElement value)
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

    private static string ElementToString(JsonElement cell) => cell.ValueKind switch
    {
        JsonValueKind.String => cell.GetString() ?? "",
        JsonValueKind.Number => cell.GetRawText(),
        JsonValueKind.True => "بله",
        JsonValueKind.False => "خیر",
        JsonValueKind.Null => "",
        _ => cell.GetRawText(),
    };

    private static string NormalizeDigits(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "";
        var chars = input.Where(char.IsDigit).ToArray();
        return new string(chars);
    }
}
