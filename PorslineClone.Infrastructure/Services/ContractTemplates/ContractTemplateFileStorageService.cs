using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

public class ContractTemplateFileStorageService(IHostEnvironment env)
{
    public const string RootFolderName = "ContractTemplates";

    public async Task<(string relativePath, string originalFileName)> SaveVersionAsync(
        Guid templateId,
        int versionNumber,
        IFormFile file,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(
            env.ContentRootPath ?? Directory.GetCurrentDirectory(),
            RootFolderName,
            templateId.ToString("N"));
        Directory.CreateDirectory(dir);

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".docx";

        var storedFileName = $"v{versionNumber}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}".ToLowerInvariant();
        var fullPath = Path.Combine(dir, storedFileName);

        await using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

        var relativePath = $"/{RootFolderName}/{templateId:N}/{storedFileName}".Replace('\\', '/');
        return (relativePath, file.FileName);
    }

    public string ResolveFullPath(string relativePath)
    {
        var trimmed = (relativePath ?? "").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(env.ContentRootPath ?? Directory.GetCurrentDirectory(), trimmed);
    }
}
