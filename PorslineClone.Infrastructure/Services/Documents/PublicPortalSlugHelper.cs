using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PorslineClone.Infrastructure.Services.Documents;

public static partial class PublicPortalSlugHelper
{
    public static string ToSlug(string? input, Guid? suffix = null)
    {
        if (string.IsNullOrWhiteSpace(input))
            return suffix.HasValue ? suffix.Value.ToString("N")[..12] : Guid.NewGuid().ToString("N")[..12];

        var normalized = input.Trim().ToLowerInvariant();
        normalized = normalized
            .Replace(' ', '-')
            .Replace('_', '-');
        normalized = InvalidSlugChars().Replace(normalized, "");
        normalized = MultiDash().Replace(normalized, "-").Trim('-');
        if (normalized.Length > 180) normalized = normalized[..180].TrimEnd('-');
        if (string.IsNullOrEmpty(normalized))
            normalized = suffix?.ToString("N")[..12] ?? Guid.NewGuid().ToString("N")[..12];
        return normalized;
    }

    public static async Task<string> EnsureUniqueDocumentSlugAsync(
        Func<string, Task<bool>> existsAsync,
        string title,
        Guid documentId,
        CancellationToken ct = default)
    {
        var baseSlug = ToSlug(title, documentId);
        var slug = baseSlug;
        var i = 0;
        while (await existsAsync(slug))
        {
            ct.ThrowIfCancellationRequested();
            i++;
            slug = $"{baseSlug}-{i}";
        }
        return slug;
    }

    [GeneratedRegex(@"[^a-z0-9\-\u0600-\u06ff]", RegexOptions.Compiled)]
    private static partial Regex InvalidSlugChars();

    [GeneratedRegex(@"-{2,}", RegexOptions.Compiled)]
    private static partial Regex MultiDash();
}
