using System;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Persistence;
using AIStudyHub.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIStudyHub.UnitTests;

public class DocumentProcessingQueueTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly ServiceProvider _serviceProvider;

    public DocumentProcessingQueueTests()
    {
        _factory = new TestDbContextFactory();

        var services = new ServiceCollection();
        services.AddScoped<AIStudyHub.Infrastructure.Persistence.StudyHubDbContext>(_ => _factory.CreateContext());
        _serviceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        _factory.Dispose();
    }

    private static void SeedUser(StudyHubDbContext db)
    {
        if (!db.Subscriptions.Any())
        {
            db.Subscriptions.Add(new Subscription { TierId = 1, TierName = "Free", Price = 0, MaxStorageMb = 50, TotalStorageMb = 50, AiPromptLimitPerDay = 5 });
        }
        if (!db.Users.Any(u => u.UserId == 1))
        {
            db.Users.Add(new User { UserId = 1, Username = "testuser", Email = "test@example.com", PasswordHash = "hash", Role = "STUDENT", Status = "ACTIVE", TierId = 1 });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task EnqueueJobAsync_CreatesQueuedJob()
    {
        using var context = _factory.CreateContext();
        SeedUser(context);
        var doc = new Document
        {
            DocumentId = 101,
            UserId = 1,
            Title = "Test Document",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/test.pdf",
            FileSizeMb = 1.5m,
            CreatedAt = DateTime.UtcNow
        };
        context.Documents.Add(doc);
        await context.SaveChangesAsync();

        var queue = new DocumentProcessingQueue(_serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DocumentProcessingQueue>.Instance);
        var job = await queue.EnqueueJobAsync(101);

        Assert.NotNull(job);
        Assert.Equal("QUEUED", job.Status);
        Assert.Equal(101, job.DocumentId);
        Assert.Equal(0, job.AttemptCount);
    }

    [Fact]
    public async Task EnqueueJobAsync_DuplicateActiveJob_ReturnsExistingJob()
    {
        using var context = _factory.CreateContext();
        SeedUser(context);
        var doc = new Document
        {
            DocumentId = 102,
            UserId = 1,
            Title = "Doc 2",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/doc2.pdf",
            FileSizeMb = 1.0m,
            CreatedAt = DateTime.UtcNow
        };
        context.Documents.Add(doc);
        await context.SaveChangesAsync();

        var queue = new DocumentProcessingQueue(_serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DocumentProcessingQueue>.Instance);
        var job1 = await queue.EnqueueJobAsync(102);
        var job2 = await queue.EnqueueJobAsync(102);

        Assert.Equal(job1.JobId, job2.JobId);
    }

    [Fact]
    public async Task ClaimNextJobAsync_ClaimsJobAndSetsProcessing()
    {
        using var context = _factory.CreateContext();
        SeedUser(context);
        var doc = new Document
        {
            DocumentId = 103,
            UserId = 1,
            Title = "Doc 3",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/doc3.pdf",
            FileSizeMb = 1.0m,
            CreatedAt = DateTime.UtcNow
        };
        context.Documents.Add(doc);
        await context.SaveChangesAsync();

        var queue = new DocumentProcessingQueue(_serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DocumentProcessingQueue>.Instance);
        await queue.EnqueueJobAsync(103);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var claimed = await queue.ClaimNextJobAsync("worker-1", cts.Token);

        Assert.NotNull(claimed);
        Assert.Equal("PROCESSING", claimed.Status);
        Assert.Equal("worker-1", claimed.LockedBy);
        Assert.Equal(1, claimed.AttemptCount);
    }

    [Fact]
    public async Task CompleteJobAsync_MarksJobCompleted()
    {
        using var context = _factory.CreateContext();
        SeedUser(context);
        var doc = new Document
        {
            DocumentId = 104,
            UserId = 1,
            Title = "Doc 4",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/doc4.pdf",
            FileSizeMb = 1.0m,
            CreatedAt = DateTime.UtcNow
        };
        context.Documents.Add(doc);
        await context.SaveChangesAsync();

        var queue = new DocumentProcessingQueue(_serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DocumentProcessingQueue>.Instance);
        var job = await queue.EnqueueJobAsync(104);

        await queue.CompleteJobAsync(job.JobId);

        var updated = await context.DocumentProcessingJobs.FindAsync(job.JobId);
        Assert.NotNull(updated);
        Assert.Equal("COMPLETED", updated.Status);
        Assert.NotNull(updated.CompletedAt);
    }

    [Fact]
    public async Task FailJobAsync_MarksFailedThenDeadOnMaxAttempts()
    {
        using var context = _factory.CreateContext();
        SeedUser(context);
        var doc = new Document
        {
            DocumentId = 105,
            UserId = 1,
            Title = "Doc 5",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/doc5.pdf",
            FileSizeMb = 1.0m,
            CreatedAt = DateTime.UtcNow
        };
        context.Documents.Add(doc);
        await context.SaveChangesAsync();

        var queue = new DocumentProcessingQueue(_serviceProvider.GetRequiredService<IServiceScopeFactory>(), NullLogger<DocumentProcessingQueue>.Instance);
        var job = await queue.EnqueueJobAsync(105);

        // First failure (attempt 1)
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        await queue.ClaimNextJobAsync("worker-1", cts.Token);
        await queue.FailJobAsync(job.JobId, "Parsing error");

        using (var checkDb1 = _factory.CreateContext())
        {
            var updated1 = await checkDb1.DocumentProcessingJobs.FindAsync(job.JobId);
            Assert.NotNull(updated1);
            Assert.Equal("FAILED", updated1.Status);
            Assert.Equal("Parsing error", updated1.LastError);
            updated1.AvailableAt = DateTime.UtcNow.AddMinutes(-1);
            await checkDb1.SaveChangesAsync();
        }

        // Attempt 2
        await queue.ClaimNextJobAsync("worker-1", cts.Token);
        await queue.FailJobAsync(job.JobId, "Parsing error 2");

        using (var checkDb2 = _factory.CreateContext())
        {
            var updated2 = await checkDb2.DocumentProcessingJobs.FindAsync(job.JobId);
            Assert.NotNull(updated2);
            Assert.Equal("FAILED", updated2.Status);
            updated2.AvailableAt = DateTime.UtcNow.AddMinutes(-1);
            await checkDb2.SaveChangesAsync();
        }

        // Attempt 3 (Max)
        await queue.ClaimNextJobAsync("worker-1", cts.Token);
        await queue.FailJobAsync(job.JobId, "Parsing error fatal");

        using (var checkDb3 = _factory.CreateContext())
        {
            var updated3 = await checkDb3.DocumentProcessingJobs.FindAsync(job.JobId);
            Assert.NotNull(updated3);
            Assert.Equal("DEAD", updated3.Status);
        }
    }
}
