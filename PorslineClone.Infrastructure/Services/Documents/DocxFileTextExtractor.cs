using PorslineClone.Application.Abstractions;

namespace PorslineClone.Infrastructure.Services.Documents;

public sealed class DocxFileTextExtractor : ITextExtractor
{
    public IReadOnlyCollection<string> SupportedExtensions { get; } = ["docx"];

    public bool CanExtract(string extension)
        => string.Equals(extension.Trim().TrimStart('.'), "docx", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var text = DocxTextExtractor.ExtractPlainText(filePath);
        return Task.FromResult(text);
    }
}
