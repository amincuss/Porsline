using Microsoft.AspNetCore.Http;

namespace PorslineClone.Infrastructure.Services.ContractTemplates;

/// <summary>آداپتر IFormFile برای فایل تولیدشده در حافظه/دیسک</summary>
public sealed class StreamFormFile(Stream stream, string fileName, string contentType) : IFormFile
{
    public string ContentType { get; } = contentType;
    public string ContentDisposition => $"form-data; name=\"file\"; filename=\"{fileName}\"";
    public IHeaderDictionary Headers => new HeaderDictionary();
    public long Length => stream.CanSeek ? stream.Length : 0;
    public string Name => "file";
    public string FileName { get; } = fileName;

    public void CopyTo(Stream target) => stream.CopyTo(target);

    public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        => stream.CopyToAsync(target, cancellationToken);

    public Stream OpenReadStream()
    {
        if (stream.CanSeek)
            stream.Position = 0;
        return stream;
    }
}
