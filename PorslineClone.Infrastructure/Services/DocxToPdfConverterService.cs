using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PorslineClone.Application.Abstractions;
using PorslineClone.Infrastructure.Options;

namespace PorslineClone.Infrastructure.Services;

public class DocxToPdfConverterService(
    IOptions<ContractSignatureOptions> signatureOptions,
    ILogger<DocxToPdfConverterService> logger) : IDocxToPdfConverter
{
    private readonly ContractSignatureOptions _options = signatureOptions.Value;
    private string? _cachedExecutable;

    public bool IsAvailable => FindLibreOfficeExecutable() is not null;

    public string? TryConvert(string docxFullPath)
    {
        if (!File.Exists(docxFullPath))
            return null;

        var soffice = FindLibreOfficeExecutable();
        if (soffice is null)
        {
            logger.LogInformation("LibreOffice not found; skipping DOCX to PDF conversion for {Path}", docxFullPath);
            return null;
        }

        var outDir = Path.GetDirectoryName(docxFullPath)!;
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = soffice,
                Arguments = $"--headless --nologo --nofirststartwizard --convert-to pdf --outdir \"{outDir}\" \"{docxFullPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
            proc?.WaitForExit(120_000);
            if (proc is not null && proc.ExitCode != 0)
            {
                logger.LogWarning("LibreOffice exited with code {Code} for {Path}", proc.ExitCode, docxFullPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "DOCX to PDF conversion failed for {Path}", docxFullPath);
            return null;
        }

        var pdfFull = Path.Combine(outDir, Path.GetFileNameWithoutExtension(docxFullPath) + ".pdf");
        return File.Exists(pdfFull) ? pdfFull : null;
    }

    private string? FindLibreOfficeExecutable()
    {
        if (_cachedExecutable is not null && File.Exists(_cachedExecutable))
            return _cachedExecutable;

        if (!string.IsNullOrWhiteSpace(_options.LibreOfficePath) && File.Exists(_options.LibreOfficePath))
            return _cachedExecutable = _options.LibreOfficePath;

        var candidates = new[]
        {
            "soffice",
            "soffice.exe",
            @"C:\Program Files\LibreOffice\program\soffice.exe",
            @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
        };

        foreach (var c in candidates)
        {
            if (c.Contains('\\') || c.Contains('/'))
            {
                if (File.Exists(c))
                    return _cachedExecutable = c;
                continue;
            }

            var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var full = Path.Combine(dir.Trim(), c);
                if (File.Exists(full))
                    return _cachedExecutable = full;
            }
        }

        return null;
    }
}
