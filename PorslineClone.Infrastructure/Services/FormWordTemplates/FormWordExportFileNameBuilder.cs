using System.Text.Json;
using System.Text.RegularExpressions;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services.FormWordTemplates;

/// <summary>نام فایل خروجی Word: نام_نام‌خانوادگی_کدملی.docx — هر کاربر یک فایل جدا در ZIP.</summary>
public static class FormWordExportFileNameBuilder
{
    public static string BuildDocxFileName(FormSubmission submission, Responder? responder = null)
    {
        var fields = DeserializeFields(submission.FieldsJson);

        var first = FindFirstName(fields);
        var last = FindLastName(fields);
        var national = FindNationalCode(fields);

        var combined = FindCombinedFullName(fields);
        if (!string.IsNullOrWhiteSpace(combined))
            TrySplitFullName(combined, ref first, ref last);

        if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
            TrySplitFullName(submission.SubmitterName, ref first, ref last);

        if (responder is not null)
        {
            if (string.IsNullOrWhiteSpace(national))
                national = NormalizeNationalCode(responder.NationalCode);
            if (string.IsNullOrWhiteSpace(first) || string.IsNullOrWhiteSpace(last))
                TrySplitFullName(responder.FullName, ref first, ref last);
        }

        national = NormalizeNationalCode(national);

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(first)) parts.Add(SanitizePart(first));
        if (!string.IsNullOrWhiteSpace(last)) parts.Add(SanitizePart(last));
        if (!string.IsNullOrWhiteSpace(national)) parts.Add(national);

        if (parts.Count == 0)
        {
            var fallback = submission.TrackingCode
                ?? submission.SubmitterName
                ?? responder?.FullName
                ?? submission.Id.ToString("N")[..8];
            return SanitizePart(fallback) + ".docx";
        }

        return string.Join("_", parts) + ".docx";
    }

    public static string EnsureUnique(string fileName, HashSet<string> used)
    {
        if (used.Add(fileName)) return fileName;
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (var i = 2; i < 1000; i++)
        {
            var candidate = $"{baseName}_{i}{ext}";
            if (used.Add(candidate)) return candidate;
        }
        return $"{baseName}_{Guid.NewGuid():N}{ext}";
    }

    private static string? FindFirstName(List<FormFieldValueDto> fields)
    {
        foreach (var f in fields)
        {
            var label = (f.Label ?? "").Trim();
            if (!IsFirstNameLabel(label)) continue;
            var val = CleanValue(f.Value);
            if (!string.IsNullOrEmpty(val)) return val;
        }
        return null;
    }

    private static string? FindLastName(List<FormFieldValueDto> fields)
    {
        foreach (var f in fields)
        {
            var label = (f.Label ?? "").Trim();
            if (!IsLastNameLabel(label)) continue;
            var val = CleanValue(f.Value);
            if (!string.IsNullOrEmpty(val)) return val;
        }
        return null;
    }

    private static string? FindNationalCode(List<FormFieldValueDto> fields)
    {
        foreach (var f in fields)
        {
            var label = (f.Label ?? "").Trim();
            if (!IsNationalCodeLabel(label)) continue;
            var val = NormalizeNationalCode(f.Value);
            if (val is not null) return val;
        }
        return null;
    }

    private static string? FindCombinedFullName(List<FormFieldValueDto> fields)
    {
        foreach (var f in fields)
        {
            var label = (f.Label ?? "").Trim();
            if (IsNationalCodeLabel(label)) continue;
            var isCombined = label.Contains("نام و نام", StringComparison.OrdinalIgnoreCase)
                || (label.Contains("نام", StringComparison.OrdinalIgnoreCase)
                    && label.Contains("خانوادگی", StringComparison.OrdinalIgnoreCase)
                    && !IsLastNameLabel(label));
            if (!isCombined) continue;
            var val = CleanValue(f.Value);
            if (!string.IsNullOrEmpty(val)) return val;
        }
        return null;
    }

    private static bool IsFirstNameLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        if (label.Contains("نام خانوادگی", StringComparison.OrdinalIgnoreCase)) return false;
        if (label.Contains("نام و نام", StringComparison.OrdinalIgnoreCase)) return false;
        if (label.Contains("نام پدر", StringComparison.OrdinalIgnoreCase)) return false;
        if (label.Contains("نام مادر", StringComparison.OrdinalIgnoreCase)) return false;
        if (label.Contains("نام شرکت", StringComparison.OrdinalIgnoreCase)) return false;
        if (label.Equals("نام", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.StartsWith("نام ", StringComparison.OrdinalIgnoreCase)
            && !label.Contains("خانوادگی", StringComparison.OrdinalIgnoreCase))
            return true;
        var compact = label.Replace(" ", "", StringComparison.Ordinal);
        return compact.Equals("name", StringComparison.OrdinalIgnoreCase)
               || compact.Equals("firstname", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLastNameLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        if (label.Contains("نام خانوادگی", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Contains("نامخانوادگی", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Contains("نام خانوادگي", StringComparison.OrdinalIgnoreCase)) return true;
        if (label.Contains("فامیل", StringComparison.OrdinalIgnoreCase)
            || label.Contains("فامیلی", StringComparison.OrdinalIgnoreCase))
            return true;
        var compact = label.Replace(" ", "", StringComparison.Ordinal);
        return compact.Contains("lastname", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("familyname", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNationalCodeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return false;
        var compact = label.Replace(" ", "", StringComparison.Ordinal);
        return label.Contains("کد ملی", StringComparison.OrdinalIgnoreCase)
               || label.Contains("کدملی", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("کدملی", StringComparison.OrdinalIgnoreCase)
               || label.Contains("شماره ملی", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("nationalcode", StringComparison.OrdinalIgnoreCase)
               || compact.Contains("nationalid", StringComparison.OrdinalIgnoreCase);
    }

    private static string? CleanValue(string? raw)
    {
        var v = (raw ?? "").Trim();
        if (string.IsNullOrEmpty(v) || FormSubmissionUploadHelper.IsUploadPath(v)) return null;
        return v;
    }

    private static List<FormFieldValueDto> DeserializeFields(string? json)
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

    private static string? NormalizeNationalCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var digits = Regex.Replace(raw.Trim(), @"\D", "");
        return digits.Length == 10 ? digits : null;
    }

    private static void TrySplitFullName(string? fullName, ref string? first, ref string? last)
    {
        var t = (fullName ?? "").Trim();
        if (t.Length < 2) return;
        var parts = t.Split([' ', '\u00A0', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            if (string.IsNullOrWhiteSpace(first)) first = parts[0];
            if (string.IsNullOrWhiteSpace(last)) last = string.Join(" ", parts.Skip(1));
        }
        else if (string.IsNullOrWhiteSpace(first))
        {
            first = parts[0];
        }
    }

    private static string SanitizePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var c in value.Trim())
        {
            if (invalid.Contains(c) || c is ' ' or '\t')
                sb.Append('_');
            else
                sb.Append(c);
        }
        var s = sb.ToString().Trim('_');
        while (s.Contains("__", StringComparison.Ordinal))
            s = s.Replace("__", "_", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(s) ? "export" : s[..Math.Min(s.Length, 60)];
    }
}
