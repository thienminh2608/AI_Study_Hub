using System;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Infrastructure.Services;

public class DocumentExtractionWorker : BackgroundService
{
    private readonly IDocumentProcessingQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DocumentExtractionWorker> _logger;
    private readonly string _workerId;

    public DocumentExtractionWorker(
        IDocumentProcessingQueue queue,
        IServiceProvider serviceProvider,
        ILogger<DocumentExtractionWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _workerId = $"worker-{Environment.MachineName}-{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DocumentExtractionWorker ({WorkerId}) background service started.", _workerId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _queue.ClaimNextJobAsync(_workerId, stoppingToken);
                if (job == null)
                {
                    continue;
                }

                _logger.LogInformation("Processing extraction job {JobId} for DocumentId: {DocumentId}, VersionId: {VersionId}",
                    job.JobId, job.DocumentId, job.DocumentVersionId);

                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
                        await documentService.ProcessExtractionAsync(job.DocumentId, job.DocumentVersionId);
                    }

                    await _queue.CompleteJobAsync(job.JobId);
                    _logger.LogInformation("Successfully completed extraction job {JobId} for DocumentId: {DocumentId}", job.JobId, job.DocumentId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed extraction job {JobId} for DocumentId: {DocumentId}", job.JobId, job.DocumentId);
                    await _queue.FailJobAsync(job.JobId, ex.Message);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in DocumentExtractionWorker execution loop.");
                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
