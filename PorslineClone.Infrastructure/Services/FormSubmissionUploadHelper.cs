using Microsoft.AspNetCore.Hosting;
using PorslineClone.Application.Contracts;

namespace PorslineClone.Infrastructure.Services;

/// <summary>مسیرهای آپلود پاسخ فرم (/Formupload/...) — تشخیص، نرمال‌سازی و resolve روی دیسک.</summary>
public static class FormSubmissionUploadHelper
{
    public const string PathPrefix = "/Formupload/";

    public static bool IsUploadPath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var t = value.Trim();
        if (t.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase)) return true;
        if (t.StartsWith("Formupload/", StringComparison.OrdinalIgnoreCase)) return true;
        return t.Contains("/Formupload/", StringComparison.OrdinalIgnoreCase)
               || t.Contains("Formupload/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>مسیر نسبی یکتا: /Formupload/{folder}/{file}</summary>
    public static string? NormalizeRelativePath(string? storedValue)
    {
        if (string.IsNullOrWhiteSpace(storedValue)) return null;
        var t = storedValue.Trim().Replace('\\', '/');

        if (t.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (Uri.TryCreate(t, UriKind.Absolute, out var uri))
                t = uri.AbsolutePath;
            else
                return null;
        }

        var idx = t.IndexOf("/Formupload/", StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            idx = t.IndexOf("Formupload/", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return null;
            t = "/" + t[idx..];
        }
        else
        {
            t = t[idx..];
        }

        var segments = t.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3
            || !string.Equals(segments[0], "Formupload", StringComparison.OrdinalIgnoreCase))
            return null;

        return "/" + string.Join('/', segments);
    }

    public static IReadOnlyList<string> ListUploadPaths(IEnumerable<FormFieldValueDto> values) =>
        values
            .Select(v => NormalizeRelativePath(v.Value))
            .Where(p => p is not null)
            .Cast<string>()
            .ToList();

    public static bool TryResolveDiskPath(IWebHostEnvironment env, string? storedValue, out string fullPath)
    {
        fullPath = "";
        var relative = NormalizeRelativePath(storedValue);
        if (relative is null) return false;

        var segments = relative.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var candidates = new List<string>
        {
            Path.Combine(new[] { env.ContentRootPath }.Concat(segments).ToArray()),
        };

        if (!string.IsNullOrWhiteSpace(env.WebRootPath)
            && !string.Equals(env.WebRootPath, env.ContentRootPath, StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(Path.Combine(new[] { env.WebRootPath }.Concat(segments).ToArray()));
        }

        // سازگاری با Path.Combine(ContentRoot, "Formupload\guid\file")
        var legacy = Path.Combine(
            env.ContentRootPath,
            relative.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        candidates.Add(legacy);

        foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (File.Exists(path))
            {
                fullPath = path;
                return true;
            }
        }

        fullPath = candidates[0];
        return false;
    }

    public static long ResolveSizeBytes(IWebHostEnvironment env, string? storedValue) =>
        TryResolveDiskPath(env, storedValue, out var path) ? new FileInfo(path).Length : 0L;

    public static string FileKindFromPath(string relativePath)
    {
        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        return ext switch
        {
            ".pdf" => "pdf",
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".gif" => "image",
            _ => "file",
        };
    }

}
