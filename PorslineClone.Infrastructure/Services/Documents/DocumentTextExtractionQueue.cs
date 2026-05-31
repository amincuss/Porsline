using System.Threading.Channels;
using PorslineClone.Application.Abstractions;

namespace PorslineClone.Infrastructure.Services.Documents;

public sealed class DocumentTextExtractionQueue : IDocumentTextExtractionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = false,
        SingleWriter = false,
    });

    public ValueTask EnqueueAsync(Guid documentVersionId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(documentVersionId, cancellationToken);

    internal ChannelReader<Guid> Reader => _channel.Reader;
}
