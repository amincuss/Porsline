using Microsoft.Extensions.Hosting;

namespace PorslineClone.Infrastructure.Services.FormSubmissions;

public class FormSubmissionExcelExportFileStorage(IHostEnvironment env)
{
    public const string ExportsRoot = "FormSubmissionExcelExports";

    public async Task<(string relativePath, string fileName)> SaveExcelAsync(
        Guid jobId,
        string downloadFileName,
        byte[] bytes,
        CancellationToken ct = default)
    {
        var dir = Path.Combine(Root(), ExportsRoot, jobId.ToString("N"));
        Directory.CreateDirectory(dir);
        var safe = SanitizeFileName(downloadFileName);
        if (!safe.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            safe += ".xlsx";
        var full = Path.Combine(dir, safe);
        await File.WriteAllBytesAsync(full, bytes, ct);
        var relative = $"/{ExportsRoot}/{jobId:N}/{safe}".Replace('\\', '/');
        return (relative, safe);
    }

    public string? ResolveFullPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        var trimmed = relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(Root(), trimmed);
    }

    private string Root() => env.ContentRootPath;

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "export" : cleaned;
    }
}
