using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;

namespace AIStudyHub.Infrastructure.Services;

public class DocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly Channel<int> _queue;

    public DocumentProcessingQueue()
    {
        var options = new UnboundedChannelOptions
        {
            SingleReader = true
        };
        _queue = Channel.CreateUnbounded<int>(options);
    }

    public void EnqueueDocument(int documentId)
    {
        _queue.Writer.TryWrite(documentId);
    }

    public async Task<int> DequeueAsync(CancellationToken cancellationToken)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }
}
