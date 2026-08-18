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

    public DocumentExtractionWorker(
        IDocumentProcessingQueue queue,
        IServiceProvider serviceProvider,
        ILogger<DocumentExtractionWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DocumentExtractionWorker background service started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int documentId = await _queue.DequeueAsync(stoppingToken);
                _logger.LogInformation("Processing background AI extraction for DocumentId: {DocumentId}", documentId);

                using (var scope = _serviceProvider.CreateScope())
                {
                    var documentService = scope.ServiceProvider.GetRequiredService<IDocumentService>();
                    await documentService.ProcessExtractionAsync(documentId);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred during background document extraction execution.");
            }
        }
    }
}
