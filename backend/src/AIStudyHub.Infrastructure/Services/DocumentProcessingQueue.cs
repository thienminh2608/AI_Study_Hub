using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Infrastructure.Services;

public class DocumentProcessingQueue : IDocumentProcessingQueue
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DocumentProcessingQueue> _logger;
    private readonly SemaphoreSlim _signal = new(0);

    public DocumentProcessingQueue(IServiceScopeFactory scopeFactory, ILogger<DocumentProcessingQueue> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<DocumentProcessingJob> EnqueueJobAsync(int documentId, int? versionId = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        // Check if an active job already exists for this document / version
        var existingActiveJob = await db.DocumentProcessingJobs
            .FirstOrDefaultAsync(j => j.DocumentId == documentId &&
                                      j.DocumentVersionId == versionId &&
                                      (j.Status == "QUEUED" || j.Status == "PROCESSING"));

        if (existingActiveJob != null)
        {
            _logger.LogInformation("Job already queued or processing for document {DocId} (JobId: {JobId})", documentId, existingActiveJob.JobId);
            return existingActiveJob;
        }

        var job = new DocumentProcessingJob
        {
            DocumentId = documentId,
            DocumentVersionId = versionId,
            Status = "QUEUED",
            AttemptCount = 0,
            MaxAttempts = 3,
            AvailableAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        db.DocumentProcessingJobs.Add(job);
        await db.SaveChangesAsync();

        _signal.Release();
        return job;
    }

    public async Task<DocumentProcessingJob?> ClaimNextJobAsync(string workerId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
            var now = DateTime.UtcNow;

            // Try SQL Server atomic UPDATE OUTPUT if relational database is SQL Server
            if (db.Database.IsSqlServer())
            {
                var sql = """
                    WITH NextJob AS (
                        SELECT TOP (1) *
                        FROM document_processing_jobs WITH (READPAST, UPDLOCK, ROWLOCK)
                        WHERE status IN ('QUEUED', 'FAILED')
                          AND attempt_count < max_attempts
                          AND available_at <= {0}
                          AND (locked_until IS NULL OR locked_until < {0})
                        ORDER BY created_at ASC
                    )
                    UPDATE NextJob
                    SET status = 'PROCESSING',
                        locked_at = {0},
                        locked_until = {1},
                        locked_by = {2},
                        attempt_count = attempt_count + 1
                    OUTPUT inserted.job_id, inserted.document_id, inserted.document_version_id, inserted.status, inserted.attempt_count, inserted.max_attempts, inserted.available_at, inserted.locked_at, inserted.locked_until, inserted.locked_by, inserted.last_error, inserted.created_at, inserted.completed_at;
                """;

                var leaseUntil = now.AddMinutes(5);
                var claimedList = await db.DocumentProcessingJobs
                    .FromSqlRaw(sql, now, leaseUntil, workerId)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                if (claimedList.Any())
                {
                    return claimedList.First();
                }
            }
            else
            {
                // Fallback for SQLite / Test InMemory
                var job = await db.DocumentProcessingJobs
                    .Where(j => (j.Status == "QUEUED" || j.Status == "FAILED") &&
                                j.AttemptCount < j.MaxAttempts &&
                                j.AvailableAt <= now &&
                                (j.LockedUntil == null || j.LockedUntil < now))
                    .OrderBy(j => j.CreatedAt)
                    .FirstOrDefaultAsync(cancellationToken);

                if (job != null)
                {
                    job.Status = "PROCESSING";
                    job.LockedAt = now;
                    job.LockedUntil = now.AddMinutes(5);
                    job.LockedBy = workerId;
                    job.AttemptCount += 1;

                    try
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        return job;
                    }
                    catch (DbUpdateConcurrencyException)
                    {
                        // Lost race, loop and try next
                    }
                }
            }

            // Wait for signal or timeout (5 seconds poll interval)
            try
            {
                await _signal.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        return null;
    }

    public async Task CompleteJobAsync(int jobId)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var job = await db.DocumentProcessingJobs.FindAsync(jobId);
        if (job != null)
        {
            job.Status = "COMPLETED";
            job.CompletedAt = DateTime.UtcNow;
            job.LockedUntil = null;
            job.LastError = null;
            await db.SaveChangesAsync();
        }
    }

    public async Task FailJobAsync(int jobId, string errorMessage)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var job = await db.DocumentProcessingJobs.FindAsync(jobId);
        if (job != null)
        {
            job.LastError = errorMessage;
            job.LockedUntil = null;

            if (job.AttemptCount >= job.MaxAttempts)
            {
                job.Status = "DEAD";
                _logger.LogError("Document processing job {JobId} for Document {DocId} reached max attempts and is marked DEAD. Error: {Error}", jobId, job.DocumentId, errorMessage);
            }
            else
            {
                job.Status = "FAILED";
                // Exponential backoff: 30s * attemptCount
                int backoffSeconds = 30 * Math.Max(1, job.AttemptCount);
                job.AvailableAt = DateTime.UtcNow.AddSeconds(backoffSeconds);
                _logger.LogWarning("Document processing job {JobId} for Document {DocId} failed (Attempt {Attempt}/{Max}). Scheduled retry at {Time}. Error: {Error}",
                    jobId, job.DocumentId, job.AttemptCount, job.MaxAttempts, job.AvailableAt, errorMessage);
            }

            await db.SaveChangesAsync();
        }
    }
}
