using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace PorslineClone.Infrastructure.Services.FormWordTemplates;

public class FormWordTemplateFileStorage(IHostEnvironment env)
{
    public const string TemplatesRoot = "FormWordTemplates";
    public const string ExportsRoot = "FormWordExports";
    public const string BatchExportsRoot = "FormWordBatchExports";

    public async Task<(string relativePath, string originalFileName)> SaveDocxAsync(
        Guid templateId,
        IFormFile file,
        CancellationToken ct = default)
    {
        var dir = TemplateDir(templateId);
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".docx";
        var stored = $"template_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}".ToLowerInvariant();
        var full = Path.Combine(dir, stored);
        await using (var stream = File.Create(full))
            await file.CopyToAsync(stream, ct);
        var relative = $"/{TemplatesRoot}/{templateId:N}/{stored}".Replace('\\', '/');
        return (relative, file.FileName);
    }

    public async Task<string> SaveSignatureAsync(Guid templateId, IFormFile file, CancellationToken ct = default)
        => await SaveTemplateImageAsync(templateId, file, "signature", ct);

    public async Task<string> SaveStampAsync(Guid templateId, IFormFile file, CancellationToken ct = default)
        => await SaveTemplateImageAsync(templateId, file, "stamp", ct);

    private async Task<string> SaveTemplateImageAsync(
        Guid templateId,
        IFormFile file,
        string baseName,
        CancellationToken ct)
    {
        var dir = TemplateDir(templateId);
        Directory.CreateDirectory(dir);
        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
        var stored = $"{baseName}{ext}".ToLowerInvariant();
        var full = Path.Combine(dir, stored);
        await using (var stream = File.Create(full))
            await file.CopyToAsync(stream, ct);
        return $"/{TemplatesRoot}/{templateId:N}/{stored}".Replace('\\', '/');
    }

    public async Task<(string relativePath, string fileName)> SaveExportAsync(
        Guid submissionId,
        string downloadFileName,
        string sourceTempPath,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(Root(), ExportsRoot, submissionId.ToString("N"));
        Directory.CreateDirectory(dir);
        var safe = SanitizeFileName(downloadFileName);
        if (!safe.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
            safe += ".docx";
        var full = Path.Combine(dir, safe);
        File.Copy(sourceTempPath, full, overwrite: true);
        await Task.CompletedTask;
        var relative = $"/{ExportsRoot}/{submissionId:N}/{safe}".Replace('\\', '/');
        return (relative, safe);
    }

    public async Task<(string relativePath, string fileName)> SaveBatchZipAsync(
        Guid jobId,
        string zipFileName,
        byte[] bytes,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(Root(), BatchExportsRoot, jobId.ToString("N"));
        Directory.CreateDirectory(dir);
        var safe = SanitizeFileName(zipFileName);
        if (!safe.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            safe += ".zip";
        var full = Path.Combine(dir, safe);
        await File.WriteAllBytesAsync(full, bytes, ct);
        var relative = $"/{BatchExportsRoot}/{jobId:N}/{safe}".Replace('\\', '/');
        return (relative, safe);
    }

    public string ResolveFullPath(string relativePath)
    {
        var trimmed = (relativePath ?? "").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(Root(), trimmed);
    }

    private string TemplateDir(Guid templateId) =>
        Path.Combine(Root(), TemplatesRoot, templateId.ToString("N"));

    private string Root() => env.ContentRootPath ?? Directory.GetCurrentDirectory();

    public static string SanitizeFileNamePublic(string name) => SanitizeFileName(name);

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new System.Text.StringBuilder();
        foreach (var c in (name ?? "export").Trim())
        {
            if (invalid.Contains(c) || c is ' ' or '\t')
                sb.Append('_');
            else
                sb.Append(c);
        }
        var s = sb.ToString().Trim('_');
        while (s.Contains("__", StringComparison.Ordinal))
            s = s.Replace("__", "_", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(s) ? "export" : s[..Math.Min(s.Length, 120)];
    }
}
