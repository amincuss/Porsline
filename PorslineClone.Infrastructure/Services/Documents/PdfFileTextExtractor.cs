using System.Text;
using PorslineClone.Application.Abstractions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace PorslineClone.Infrastructure.Services.Documents;

/// <summary>استخراج متن از PDF دیجیتال (بدون OCR).</summary>
public sealed class PdfFileTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = ["pdf"];

    public bool CanExtract(string extension)
        => string.Equals(extension.Trim().TrimStart('.'), "pdf", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(filePath))
            return Task.FromResult(string.Empty);

        var sb = new StringBuilder();
        using var document = PdfDocument.Open(filePath);
        foreach (var page in document.GetPages())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var words = page.GetWords().ToList();
            if (words.Count == 0)
                continue;

            var line = string.Join(" ", words.Select(w => w.Text));
            if (!string.IsNullOrWhiteSpace(line))
                sb.AppendLine(line.Trim());
        }

        return Task.FromResult(sb.ToString().Trim());
    }
}
