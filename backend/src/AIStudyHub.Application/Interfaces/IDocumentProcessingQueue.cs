using System.Threading.Tasks;

namespace AIStudyHub.Application.Interfaces;

public interface IDocumentProcessingQueue
{
    void EnqueueDocument(int documentId);
    Task<int> DequeueAsync(System.Threading.CancellationToken cancellationToken);
}
