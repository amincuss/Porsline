using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace PorslineClone.Infrastructure.Services;

public class DocumentFileStorageService(IHostEnvironment env)
{
    public const string RootFolderName = "Documents";

    public async Task<(string relativePath, string originalFileName, string extension)> SaveAsync(
        Guid documentId,
        int versionNumber,
        IFormFile file,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var folder = Path.Combine(
            env.ContentRootPath ?? Directory.GetCurrentDirectory(),
            RootFolderName,
            now.ToString("yyyy"),
            now.ToString("MM"),
            documentId.ToString("N"));
        Directory.CreateDirectory(folder);

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".bin";
        ext = ext.ToLowerInvariant();

        var name = $"v{versionNumber}_{now:yyyyMMddHHmmssfff}{ext}";
        var fullPath = Path.Combine(folder, name);
        await using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

        var relative = $"/{RootFolderName}/{now:yyyy}/{now:MM}/{documentId:N}/{name}".Replace('\\', '/');
        return (relative, file.FileName, ext.TrimStart('.'));
    }

    public string ResolveFullPath(string relativePath)
    {
        var trimmed = (relativePath ?? "").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(env.ContentRootPath ?? Directory.GetCurrentDirectory(), trimmed);
    }
}
