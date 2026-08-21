using System;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Domain.Entities;

namespace AIStudyHub.Application.Interfaces;

public interface IDocumentProcessingQueue
{
    Task<DocumentProcessingJob> EnqueueJobAsync(int documentId, int? versionId = null);
    Task<DocumentProcessingJob?> ClaimNextJobAsync(string workerId, CancellationToken cancellationToken);
    Task CompleteJobAsync(int jobId);
    Task FailJobAsync(int jobId, string errorMessage);
}
