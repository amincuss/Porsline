using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace PorslineClone.Infrastructure.Services;

/// <summary>
/// ذخیره فایل قرارداد در پوشه Contracts/{کدملی}/
/// </summary>
public class ContractFileStorageService(IHostEnvironment env)
{
    public const string RootFolderName = "Contracts";

    public async Task<(string relativePath, string originalFileName)> SaveAsync(
        string nationalId,
        int versionNumber,
        string? contractNumber,
        IFormFile file,
        CancellationToken ct = default)
    {
        var folder = GetNationalIdFolder(nationalId);
        var dir = Path.Combine(env.ContentRootPath ?? Directory.GetCurrentDirectory(), RootFolderName, folder);
        Directory.CreateDirectory(dir);

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext))
            ext = ".bin";

        var numberToken = SanitizeFileToken(contractNumber ?? "contract");
        var storedFileName = $"v{versionNumber}_{numberToken}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}".ToLowerInvariant();
        var fullPath = Path.Combine(dir, storedFileName);

        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

        var relativePath = $"/{RootFolderName}/{folder}/{storedFileName}".Replace('\\', '/');
        return (relativePath, file.FileName);
    }

    /// <summary>کپی فایل PDF همراه از مسیر محلی (پس از تبدیل LibreOffice)</summary>
    public async Task<string?> SavePdfCompanionAsync(
        string nationalId,
        int versionNumber,
        string? contractNumber,
        string sourcePdfFullPath,
        CancellationToken ct = default)
    {
        if (!File.Exists(sourcePdfFullPath))
            return null;

        var folder = GetNationalIdFolder(nationalId);
        var dir = Path.Combine(env.ContentRootPath ?? Directory.GetCurrentDirectory(), RootFolderName, folder);
        Directory.CreateDirectory(dir);

        var numberToken = SanitizeFileToken(contractNumber ?? "contract");
        var storedFileName = $"v{versionNumber}_{numberToken}_{DateTime.UtcNow:yyyyMMddHHmmssfff}.pdf".ToLowerInvariant();
        var fullPath = Path.Combine(dir, storedFileName);

        await using (var src = File.OpenRead(sourcePdfFullPath))
        await using (var dest = File.Create(fullPath))
            await src.CopyToAsync(dest, ct);

        return $"/{RootFolderName}/{folder}/{storedFileName}".Replace('\\', '/');
    }

    public string ResolveFullPath(string relativePath)
    {
        var trimmed = (relativePath ?? "").TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        return Path.Combine(env.ContentRootPath ?? Directory.GetCurrentDirectory(), trimmed);
    }

    public static string GetNationalIdFolder(string nationalId)
    {
        var digits = new string((nationalId ?? "").Where(char.IsDigit).ToArray());
        if (digits.Length != 10)
            throw new ArgumentException("کد ملی باید ۱۰ رقم باشد", nameof(nationalId));
        return digits;
    }

    private static string SanitizeFileToken(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "contract" : cleaned;
    }
}
