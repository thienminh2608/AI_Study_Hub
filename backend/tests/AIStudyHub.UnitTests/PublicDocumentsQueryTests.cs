using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using Xunit;

namespace AIStudyHub.UnitTests;

public class PublicDocumentsQueryTests : IDisposable
{
    private readonly TestDbContextFactory _factory;

    public PublicDocumentsQueryTests()
    {
        _factory = new TestDbContextFactory();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    private static void SeedData(AIStudyHub.Infrastructure.Persistence.StudyHubDbContext db)
    {
        if (!db.Subscriptions.Any())
        {
            db.Subscriptions.Add(new Subscription { TierId = 1, TierName = "Free", Price = 0, MaxStorageMb = 50, TotalStorageMb = 50, AiPromptLimitPerDay = 5 });
        }
        if (!db.Users.Any(u => u.UserId == 1))
        {
            db.Users.Add(new User { UserId = 1, Username = "testUploader", Email = "uploader@test.com", PasswordHash = "hash", Role = "STUDENT", Status = "ACTIVE", TierId = 1 });
        }

        // Public documents
        db.Documents.Add(new Document
        {
            DocumentId = 1,
            UserId = 1,
            Title = "Toan Giai Tich 1",
            Subject = "Toán học",
            FileExtension = "pdf",
            CloudStorageUrl = "/u/1.pdf",
            SharingPermission = "PUBLIC",
            IsFlagged = false,
            IsDeleted = false,
            DownloadCount = 50,
            ViewCount = 100,
            CreatedAt = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc)
        });

        db.Documents.Add(new Document
        {
            DocumentId = 2,
            UserId = 1,
            Title = "Lap Trinh C++ Can Ban",
            Subject = "Tin học",
            FileExtension = "docx",
            CloudStorageUrl = "/u/2.docx",
            SharingPermission = "PUBLIC",
            IsFlagged = false,
            IsDeleted = false,
            DownloadCount = 120,
            ViewCount = 300,
            CreatedAt = new DateTime(2026, 2, 15, 0, 0, 0, DateTimeKind.Utc)
        });

        db.Documents.Add(new Document
        {
            DocumentId = 3,
            UserId = 1,
            Title = "Co So Du Lieu Nang Cao",
            Subject = "Tin học",
            FileExtension = "pdf",
            CloudStorageUrl = "/u/3.pdf",
            SharingPermission = "PUBLIC",
            IsFlagged = false,
            IsDeleted = false,
            DownloadCount = 80,
            ViewCount = 200,
            CreatedAt = new DateTime(2026, 3, 20, 0, 0, 0, DateTimeKind.Utc)
        });

        // Private / Flagged / Deleted documents (must NOT appear in public query)
        db.Documents.Add(new Document
        {
            DocumentId = 4,
            UserId = 1,
            Title = "Private Notes",
            Subject = "Tin học",
            FileExtension = "pdf",
            CloudStorageUrl = "/u/4.pdf",
            SharingPermission = "PRIVATE",
            IsFlagged = false,
            IsDeleted = false
        });

        db.Documents.Add(new Document
        {
            DocumentId = 5,
            UserId = 1,
            Title = "Flagged Bad Content",
            Subject = "Toán học",
            FileExtension = "pdf",
            CloudStorageUrl = "/u/5.pdf",
            SharingPermission = "PUBLIC",
            IsFlagged = true,
            IsDeleted = false
        });

        db.Documents.Add(new Document
        {
            DocumentId = 6,
            UserId = 1,
            Title = "Deleted Public Doc",
            Subject = "Toán học",
            FileExtension = "pdf",
            CloudStorageUrl = "/u/6.pdf",
            SharingPermission = "PUBLIC",
            IsFlagged = false,
            IsDeleted = true
        });

        db.SaveChanges();
    }

    [Fact]
    public async Task GetPublicDocumentsPagedAsync_Filters_By_Subject()
    {
        using var db = _factory.CreateContext();
        SeedData(db);

        var service = new DocumentService(db, null!, new TestClock(), null!, new AIStudyHub.Infrastructure.Services.Gemini.MockGeminiService(), null!);

        // Filter by 'Tin học'
        var result = await service.GetPublicDocumentsPagedAsync(
            pageNumber: 1,
            pageSize: 10,
            search: null,
            subject: "Tin học",
            extensions: null,
            sortBy: "createdAt",
            sortDirection: "desc");

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, doc => Assert.Equal("Tin học", doc.Subject));
        Assert.DoesNotContain(result.Items, doc => doc.DocumentId == 1); // Toán học
        Assert.DoesNotContain(result.Items, doc => doc.DocumentId == 4); // Private
        Assert.DoesNotContain(result.Items, doc => doc.DocumentId == 5); // Flagged
        Assert.DoesNotContain(result.Items, doc => doc.DocumentId == 6); // Deleted
    }

    [Fact]
    public async Task GetPublicDocumentsPagedAsync_Subject_All_Returns_All_Public_Documents()
    {
        using var db = _factory.CreateContext();
        SeedData(db);

        var service = new DocumentService(db, null!, new TestClock(), null!, new AIStudyHub.Infrastructure.Services.Gemini.MockGeminiService(), null!);

        var result = await service.GetPublicDocumentsPagedAsync(
            pageNumber: 1,
            pageSize: 10,
            search: null,
            subject: "ALL",
            extensions: null,
            sortBy: "createdAt",
            sortDirection: "desc");

        Assert.Equal(3, result.TotalCount);
    }

    [Fact]
    public async Task GetPublicDocumentsPagedAsync_Combined_Subject_Extension_Search()
    {
        using var db = _factory.CreateContext();
        SeedData(db);

        var service = new DocumentService(db, null!, new TestClock(), null!, new AIStudyHub.Infrastructure.Services.Gemini.MockGeminiService(), null!);

        // Subject: Tin học, Extension: pdf, Search: "Co So"
        var result = await service.GetPublicDocumentsPagedAsync(
            pageNumber: 1,
            pageSize: 10,
            search: "Co So",
            subject: "Tin học",
            extensions: new List<string> { "pdf" },
            sortBy: "createdAt",
            sortDirection: "desc");

        Assert.Equal(1, result.TotalCount);
        Assert.Equal(3, result.Items[0].DocumentId);
        Assert.Equal("Co So Du Lieu Nang Cao", result.Items[0].Title);
    }

    [Fact]
    public async Task GetPublicDocumentsPagedAsync_Deterministic_Secondary_Sort()
    {
        using var db = _factory.CreateContext();
        SeedData(db);

        var service = new DocumentService(db, null!, new TestClock(), null!, new AIStudyHub.Infrastructure.Services.Gemini.MockGeminiService(), null!);

        var resultDesc = await service.GetPublicDocumentsPagedAsync(
            pageNumber: 1,
            pageSize: 10,
            search: null,
            subject: null,
            extensions: null,
            sortBy: "downloads",
            sortDirection: "desc");

        // Downloads: 120 (Doc 2), 80 (Doc 3), 50 (Doc 1)
        Assert.Equal(3, resultDesc.TotalCount);
        Assert.Equal(2, resultDesc.Items[0].DocumentId);
        Assert.Equal(3, resultDesc.Items[1].DocumentId);
        Assert.Equal(1, resultDesc.Items[2].DocumentId);
    }

    [Fact]
    public async Task GetPublicDocumentsPagedAsync_TieBreaks_Deterministically_By_DocumentId()
    {
        using var db = _factory.CreateContext();
        if (!db.Subscriptions.Any())
        {
            db.Subscriptions.Add(new Subscription { TierId = 1, TierName = "Free", Price = 0, MaxStorageMb = 50, TotalStorageMb = 50, AiPromptLimitPerDay = 5 });
        }
        if (!db.Users.Any(u => u.UserId == 1))
        {
            db.Users.Add(new User { UserId = 1, Username = "testUploader", Email = "uploader@test.com", PasswordHash = "hash", Role = "STUDENT", Status = "ACTIVE", TierId = 1 });
        }

        // Add 3 documents with the EXACT SAME download count (100) and created date
        db.Documents.Add(new Document { DocumentId = 30, UserId = 1, Title = "Doc Thirty", Subject = "Math", FileExtension = "pdf", CloudStorageUrl = "/u/30.pdf", SharingPermission = "PUBLIC", IsFlagged = false, DownloadCount = 100, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        db.Documents.Add(new Document { DocumentId = 10, UserId = 1, Title = "Doc Ten", Subject = "Math", FileExtension = "pdf", CloudStorageUrl = "/u/10.pdf", SharingPermission = "PUBLIC", IsFlagged = false, DownloadCount = 100, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        db.Documents.Add(new Document { DocumentId = 20, UserId = 1, Title = "Doc Twenty", Subject = "Math", FileExtension = "pdf", CloudStorageUrl = "/u/20.pdf", SharingPermission = "PUBLIC", IsFlagged = false, DownloadCount = 100, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) });
        await db.SaveChangesAsync();

        var service = new DocumentService(db, null!, new TestClock(), null!, new AIStudyHub.Infrastructure.Services.Gemini.MockGeminiService(), null!);

        // Ascending sort by downloads -> with same downloads, secondary sort is DocumentId ascending (10, 20, 30)
        var resultAsc = await service.GetPublicDocumentsPagedAsync(1, 10, null, null, null, "downloads", "asc");
        Assert.Equal(3, resultAsc.TotalCount);
        Assert.Equal(10, resultAsc.Items[0].DocumentId);
        Assert.Equal(20, resultAsc.Items[1].DocumentId);
        Assert.Equal(30, resultAsc.Items[2].DocumentId);

        // Descending sort by downloads -> with same downloads, secondary sort is DocumentId descending (30, 20, 10)
        var resultDesc = await service.GetPublicDocumentsPagedAsync(1, 10, null, null, null, "downloads", "desc");
        Assert.Equal(3, resultDesc.TotalCount);
        Assert.Equal(30, resultDesc.Items[0].DocumentId);
        Assert.Equal(20, resultDesc.Items[1].DocumentId);
        Assert.Equal(10, resultDesc.Items[2].DocumentId);
    }
}
