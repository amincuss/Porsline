using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace PorslineClone.Infrastructure.Services;

public class UserSignatureStorageService(IHostEnvironment env)
{
    public const string RootFolderName = "UserSignatures";
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase) { ".png" };

    public async Task<string> SaveAsync(Guid userId, IFormFile file, CancellationToken ct = default)
    {
        if (file.Length <= 0) throw new ArgumentException("فایل امضا ارسال نشده است");
        if (file.Length > MaxBytes) throw new ArgumentException("حداکثر حجم تصویر امضا 2MB است");

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExt.Contains(ext))
            throw new ArgumentException("امضا باید فقط با فرمت PNG باشد");

        var folder = Path.Combine(env.ContentRootPath ?? Directory.GetCurrentDirectory(), RootFolderName, userId.ToString());
        Directory.CreateDirectory(folder);

        foreach (var old in Directory.EnumerateFiles(folder))
            File.Delete(old);

        var fileName = $"signature_{DateTime.UtcNow:yyyyMMddHHmmssfff}.png";
        var fullPath = Path.Combine(folder, fileName);
        await using (var fs = File.Create(fullPath))
            await file.CopyToAsync(fs, ct);

        return $"/{RootFolderName}/{userId}/{fileName}".Replace('\\', '/');
    }

    public static string ResolveFullPath(IHostEnvironment env, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return "";
        var relative = relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(env.ContentRootPath ?? Directory.GetCurrentDirectory(), relative);
    }
}
