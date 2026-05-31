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
        await using var stream = file.OpenReadStream();
        return await SavePlainStreamAsync(documentId, versionNumber, stream, file.FileName, ct);
    }

    public async Task<(string relativePath, string originalFileName, string extension)> SavePlainBytesAsync(
        Guid documentId,
        int versionNumber,
        string originalFileName,
        byte[] bytes,
        CancellationToken ct = default)
    {
        await using var stream = new MemoryStream(bytes);
        return await SavePlainStreamAsync(documentId, versionNumber, stream, originalFileName, ct);
    }

    public async Task<(string relativePath, string originalFileName, string extension)> SavePlainStreamAsync(
        Guid documentId,
        int versionNumber,
        Stream stream,
        string originalFileName,
        CancellationToken ct = default)
    {
        var (folder, name, ext, relative) = BuildStorageLocation(documentId, versionNumber, originalFileName);
        Directory.CreateDirectory(folder);
        var fullPath = Path.Combine(folder, name);
        await using (var fs = File.Create(fullPath))
            await stream.CopyToAsync(fs, ct);

        return (relative, originalFileName, ext.TrimStart('.'));
    }

    public async Task<(string relativePath, string originalFileName, string extension)> SaveEncryptedBlobAsync(
        Guid documentId,
        int versionNumber,
        string originalFileName,
        byte[] ciphertext,
        byte[] tag,
        CancellationToken ct = default)
    {
        var (folder, name, ext, relative) = BuildStorageLocation(documentId, versionNumber, originalFileName);
        Directory.CreateDirectory(folder);
        var fullPath = Path.Combine(folder, name);
        await using (var fs = File.Create(fullPath))
        {
            await fs.WriteAsync(ciphertext, ct);
            await fs.WriteAsync(tag, ct);
        }

        return (relative, originalFileName, ext.TrimStart('.'));
    }

    public string ResolveFullPath(string relativePath)
    {
        var trimmed = (relativePath ?? "").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(env.ContentRootPath ?? Directory.GetCurrentDirectory(), trimmed);
    }

    private (string folder, string fileName, string ext, string relativePath) BuildStorageLocation(
        Guid documentId,
        int versionNumber,
        string originalFileName)
    {
        var now = DateTime.UtcNow;
        var folder = Path.Combine(
            env.ContentRootPath ?? Directory.GetCurrentDirectory(),
            RootFolderName,
            now.ToString("yyyy"),
            now.ToString("MM"),
            documentId.ToString("N"));

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".bin";
        ext = ext.ToLowerInvariant();

        var name = $"v{versionNumber}_{now:yyyyMMddHHmmssfff}{ext}";
        var relative = $"/{RootFolderName}/{now:yyyy}/{now:MM}/{documentId:N}/{name}".Replace('\\', '/');
        return (folder, name, ext, relative);
    }
}
