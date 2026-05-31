using PorslineClone.Application.Abstractions;

namespace PorslineClone.Infrastructure.Services.Documents;

public sealed class TextExtractorResolver(IEnumerable<ITextExtractor> extractors)
{
    public ITextExtractor? Resolve(string extension)
    {
        var ext = extension.Trim().TrimStart('.');
        return extractors.FirstOrDefault(x => x.CanExtract(ext));
    }
}
