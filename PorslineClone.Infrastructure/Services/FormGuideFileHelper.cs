using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace PorslineClone.Infrastructure.Services;

public static class FormGuideFileHelper
{
    public const string PathPrefix = "/FormGuide/";
    public const int MaxSizeMb = 25;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx",
        ".jpg", ".jpeg", ".png", ".webp", ".gif",
    };

    public static bool IsAllowedExtension(string ext) => AllowedExtensions.Contains(ext);

    public static string BuildRelativePath(Guid formId, Guid fieldId, string extension)
        => $"{PathPrefix}{formId:N}/{fieldId:N}/guide{extension.ToLowerInvariant()}";

    public static bool TryResolveDiskPath(IWebHostEnvironment env, string? relativePath, out string fullPath)
    {
        fullPath = "";
        if (string.IsNullOrWhiteSpace(relativePath) || !relativePath.StartsWith(PathPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var segments = relativePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 4 || !string.Equals(segments[0], "FormGuide", StringComparison.OrdinalIgnoreCase))
            return false;

        fullPath = Path.Combine(env.ContentRootPath, Path.Combine(segments));
        return File.Exists(fullPath);
    }

    public static (bool Ok, string? Error, string? RelativePath, string? DisplayName) ValidateAndBuildPath(
        Guid formId, Guid fieldId, IFormFile file)
    {
        if (file.Length <= 0) return (false, "فایل خالی است", null, null);
        var maxBytes = MaxSizeMb * 1024L * 1024L;
        if (file.Length > maxBytes) return (false, $"حجم فایل بیشتر از {MaxSizeMb}MB است", null, null);

        var ext = Path.GetExtension(file.FileName);
        if (!IsAllowedExtension(ext))
            return (false, "فرمت مجاز: PDF، Word، Excel یا تصویر", null, null);

        var safeName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(safeName)) safeName = $"guide{ext}";

        return (true, null, BuildRelativePath(formId, fieldId, ext), safeName);
    }
}
