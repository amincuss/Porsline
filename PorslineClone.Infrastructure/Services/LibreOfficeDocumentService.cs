using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PorslineClone.Application.Abstractions;
using PorslineClone.Infrastructure.Options;

namespace PorslineClone.Infrastructure.Services;

public class LibreOfficeDocumentService(
    IOptions<ContractSignatureOptions> signatureOptions,
    ILogger<LibreOfficeDocumentService> logger) : ILibreOfficeDocumentService
{
    private readonly ContractSignatureOptions _options = signatureOptions.Value;
    private string? _cachedExecutable;

    public bool IsAvailable => FindLibreOfficeExecutable() is not null;

    public string? TryConvertToPdf(string inputFullPath) =>
        RunConversion(inputFullPath, "pdf", ".pdf");

    public string? TryExtractPlainText(string inputFullPath)
    {
        if (!File.Exists(inputFullPath))
            return null;

        var ext = Path.GetExtension(inputFullPath).ToLowerInvariant();
        if (ext == ".txt")
        {
            try
            {
                return File.ReadAllText(inputFullPath);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Reading txt failed for {Path}", inputFullPath);
                return null;
            }
        }

        var txtPath = RunConversion(inputFullPath, "txt:Text", ".txt");
        if (txtPath is null) return null;
        try
        {
            return File.ReadAllText(txtPath);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Reading converted txt failed for {Path}", txtPath);
            return null;
        }
    }

    private string? RunConversion(string inputFullPath, string convertTo, string expectedExtension)
    {
        if (!File.Exists(inputFullPath))
            return null;

        var soffice = FindLibreOfficeExecutable();
        if (soffice is null)
        {
            logger.LogInformation("LibreOffice not found; skipping conversion for {Path}", inputFullPath);
            return null;
        }

        var outDir = Path.Combine(Path.GetTempPath(), "porsline-lo", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = soffice,
                Arguments =
                    $"--headless --nologo --nofirststartwizard --convert-to {convertTo} --outdir \"{outDir}\" \"{inputFullPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
            });
            proc?.WaitForExit(45_000);
            if (proc is not null && proc.ExitCode != 0)
            {
                logger.LogWarning("LibreOffice exited with code {Code} for {Path}", proc.ExitCode, inputFullPath);
            }

            var baseName = Path.GetFileNameWithoutExtension(inputFullPath);
            var expected = Path.Combine(outDir, baseName + expectedExtension);
            if (File.Exists(expected)) return expected;

            var match = Directory.GetFiles(outDir, $"*{expectedExtension}").FirstOrDefault();
            return match;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LibreOffice conversion failed for {Path}", inputFullPath);
            return null;
        }
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
