namespace PorslineClone.Infrastructure.Services;

/// <summary>ساخت URL عمومی آواتار با بررسی وجود فایل روی دیسک.</summary>
public static class ProfileAvatarUrlHelper
{
    public static string? BuildPublicUrl(string contentRoot, Guid userId, string? avatarPath)
    {
        var resolved = Resolve(contentRoot, userId, avatarPath);
        if (resolved is null) return null;
        var version = File.GetLastWriteTimeUtc(resolved.FullPath).Ticks;
        return $"{resolved.WebPath}?v={version}";
    }

    /// <summary>
    /// مسیر وب فایل موجود؛ اگر مسیر DB نامعتبر باشد آخرین avatar_* در پوشه پروفایل استفاده می‌شود.
    /// </summary>
    public static ResolvedAvatar? Resolve(string contentRoot, Guid userId, string? avatarPath)
    {
        var storedPath = StripQuery(avatarPath);
        if (!string.IsNullOrWhiteSpace(storedPath))
        {
            var relative = storedPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(contentRoot, relative);
            if (File.Exists(fullPath))
                return new ResolvedAvatar(ToWebPath(storedPath), fullPath, null);
        }

        var profileDir = Path.Combine(contentRoot, "ProfileImages", userId.ToString(), "profile");
        if (!Directory.Exists(profileDir)) return null;

        FileInfo? latest = null;
        foreach (var file in Directory.EnumerateFiles(profileDir, "avatar_*"))
        {
            var info = new FileInfo(file);
            if (latest is null || info.LastWriteTimeUtc > latest.LastWriteTimeUtc)
                latest = info;
        }

        if (latest is null) return null;

        var webPath = $"/ProfileImages/{userId}/profile/{latest.Name}";
        var needsRepair = string.IsNullOrWhiteSpace(storedPath) ||
                          !string.Equals(storedPath, webPath, StringComparison.OrdinalIgnoreCase);
        return new ResolvedAvatar(webPath, latest.FullName, needsRepair ? webPath : null);
    }

    private static string? StripQuery(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var q = path.IndexOf('?', StringComparison.Ordinal);
        return q >= 0 ? path[..q] : path;
    }

    private static string ToWebPath(string path) =>
        path.Trim().Replace('\\', '/');

    public sealed record ResolvedAvatar(string WebPath, string FullPath, string? RepairedDbPath);
}
