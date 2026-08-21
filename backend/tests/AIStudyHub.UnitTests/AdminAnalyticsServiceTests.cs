using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIStudyHub.UnitTests;

public class AdminAnalyticsServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly TestStudyHubDbContext _db;
    private readonly TestClock _clock;
    private readonly AdminAnalyticsService _analyticsService;

    public AdminAnalyticsServiceTests()
    {
        _factory = new TestDbContextFactory();
        _db = _factory.CreateContext();
        _clock = new TestClock
        {
            Now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc),
            UtcNow = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc)
        };

        _analyticsService = new AdminAnalyticsService(_db, _clock, NullLogger<AdminAnalyticsService>.Instance);
        SeedTiersAsync().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
    }

    private async Task SeedTiersAsync()
    {
        _db.Subscriptions.AddRange(
            new Subscription { TierId = 1, TierName = "Free", Price = 0m, MaxStorageMb = 50, AiPromptLimitPerDay = 5, TotalStorageMb = 50 },
            new Subscription { TierId = 2, TierName = "Basic", Price = 0m, MaxStorageMb = 200, AiPromptLimitPerDay = 20, TotalStorageMb = 200 },
            new Subscription { TierId = 3, TierName = "Premium", Price = 100_000m, MaxStorageMb = 500, AiPromptLimitPerDay = 100, TotalStorageMb = 500 }
        );
        _db.ReportReasonConfigs.Add(new ReportReasonConfig
        {
            ReasonCode = "COPYRIGHT",
            SeverityLevel = "HIGH",
            BaseScore = 1,
            AutoFlagThreshold = 5,
            Description = "Bản quyền"
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task ContributionScore_Calculates_Formula_Correctly()
    {
        // 1. Create Target User, admin and other downloader/bookmarker users
        var admin = new User { UserId = 999, Username = "admin", Email = "admin@test.com", Role = "ADMIN", Status = "ACTIVE", TierId = 2 };
        var user = new User
        {
            UserId = 501,
            Username = "author1",
            Email = "author1@test.com",
            Role = "STUDENT",
            Status = "ACTIVE",
            TierId = 2
        };
        var peer1 = new User { UserId = 502, Username = "peer1", Email = "peer1@test.com", Role = "STUDENT", Status = "ACTIVE", TierId = 2 };
        var peer2 = new User { UserId = 503, Username = "peer2", Email = "peer2@test.com", Role = "STUDENT", Status = "ACTIVE", TierId = 2 };
        _db.Users.AddRange(admin, user, peer1, peer2);
        await _db.SaveChangesAsync();

        // 2. Add 5 Approved Public Documents (5 * 2 = 10)
        for (int i = 1; i <= 5; i++)
        {
            _db.Documents.Add(new Document
            {
                DocumentId = 100 + i,
                UserId = 501,
                Title = $"Public Doc {i}",
                SharingPermission = "PUBLIC",
                ModerationStatus = "APPROVED",
                IsDeleted = false,
                IsFlagged = false,
                CloudStorageUrl = "https://storage.test/doc.pdf",
                FileExtension = "pdf",
                Subject = "Toán"
            });
        }

        // 3. Add 1 Deleted Document removed by moderator (-15)
        _db.Documents.Add(new Document
        {
            DocumentId = 110,
            UserId = 501,
            Title = "Removed Doc",
            SharingPermission = "PRIVATE",
            IsDeleted = true,
            DeletedByUserId = 999, // Admin removed
            CloudStorageUrl = "https://storage.test/removed.pdf",
            FileExtension = "pdf",
            Subject = "Lý"
        });

        await _db.SaveChangesAsync();

        // 4. Add 2 unique downloads from peer1 and peer2 on doc 101 (2 * 0.1 = 0.2)
        _db.DocumentActivities.AddRange(
            new DocumentActivity { DocumentId = 101, UserId = 502, ActivityType = "DOWNLOAD", CreatedAt = _clock.Now },
            new DocumentActivity { DocumentId = 101, UserId = 503, ActivityType = "DOWNLOAD", CreatedAt = _clock.Now },
            // Self-download should be ignored:
            new DocumentActivity { DocumentId = 101, UserId = 501, ActivityType = "DOWNLOAD", CreatedAt = _clock.Now }
        );

        // 5. Add 2 unique bookmarks from peer1 and peer2 on doc 101 (2 * 0.5 = 1.0)
        _db.Bookmarks.AddRange(
            new Bookmark { DocumentId = 101, UserId = 502, CreatedAt = _clock.Now },
            new Bookmark { DocumentId = 101, UserId = 503, CreatedAt = _clock.Now },
            // Self-bookmark should be ignored:
            new Bookmark { DocumentId = 101, UserId = 501, CreatedAt = _clock.Now }
        );

        // 6. Add 1 Upheld Violation report (-10)
        _db.DocumentReports.Add(new DocumentReport
        {
            DocumentId = 101,
            ReporterId = 502,
            ReasonCode = "COPYRIGHT",
            Status = "ACTION_TAKEN",
            CreatedAt = _clock.Now
        });

        await _db.SaveChangesAsync();

        // 7. Calculate score
        // ApprovedPublic: 5 (5*2 = 10)
        // ModerationApproved: 5 (5*3 = 15)
        // UniqueDownloads: 2 (2*0.1 = 0.2)
        // UniqueBookmarks: 2 (2*0.5 = 1.0)
        // UpheldViolations: 1 (1*-10 = -10)
        // RemovedDocuments: 1 (1*-15 = -15)
        // Expected: 10 + 15 + 0.2 + 1.0 - 10 - 15 = 1.2
        var result = await _analyticsService.CalculateUserContributionScoreAsync(501);

        Assert.Equal(501, result.UserId);
        Assert.Equal(5, result.ApprovedPublicDocuments);
        Assert.Equal(2, result.UniqueDownloads);
        Assert.Equal(2, result.UniqueBookmarks);
        Assert.Equal(5, result.ModerationApprovedDocuments);
        Assert.Equal(1, result.UpheldViolations);
        Assert.Equal(1, result.RemovedDocuments);
        Assert.Equal(1.2, result.ContributionScore);
    }

    [Fact]
    public async Task Self_Downloads_And_Self_Bookmarks_Are_Excluded_From_Score()
    {
        var user = new User { UserId = 601, Username = "author2", Email = "author2@test.com", Role = "STUDENT", Status = "ACTIVE", TierId = 2 };
        _db.Users.Add(user);
        var doc = new Document
        {
            DocumentId = 201,
            UserId = 601,
            Title = "Doc 201",
            SharingPermission = "PUBLIC",
            ModerationStatus = "APPROVED",
            IsDeleted = false,
            IsFlagged = false,
            CloudStorageUrl = "https://storage.test/doc201.pdf",
            FileExtension = "pdf",
            Subject = "Hóa"
        };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        // Only author downloaded and bookmarked own doc
        _db.DocumentActivities.Add(new DocumentActivity { DocumentId = 201, UserId = 601, ActivityType = "DOWNLOAD", CreatedAt = _clock.Now });
        _db.Bookmarks.Add(new Bookmark { DocumentId = 201, UserId = 601, CreatedAt = _clock.Now });
        await _db.SaveChangesAsync();

        var result = await _analyticsService.CalculateUserContributionScoreAsync(601);
        Assert.Equal(0, result.UniqueDownloads);
        Assert.Equal(0, result.UniqueBookmarks);
    }
}
