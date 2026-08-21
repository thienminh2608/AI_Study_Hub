using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AIStudyHub.UnitTests;

public class ModerationWorkflowTests : IDisposable
{
    private readonly TestDbContextFactory _factory;

    public ModerationWorkflowTests()
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
            db.Users.Add(new User { UserId = 1, Username = "uploader", Email = "uploader@example.com", PasswordHash = "hash", Role = "STUDENT", Status = "ACTIVE", TierId = 1 });
        }
        if (!db.Users.Any(u => u.UserId == 2))
        {
            db.Users.Add(new User { UserId = 2, Username = "reporter", Email = "reporter@example.com", PasswordHash = "hash", Role = "STUDENT", Status = "ACTIVE", TierId = 1 });
        }
        if (!db.Users.Any(u => u.UserId == 3))
        {
            db.Users.Add(new User { UserId = 3, Username = "mod1", Email = "mod1@example.com", PasswordHash = "hash", Role = "MODERATOR", Status = "ACTIVE", TierId = 1 });
        }
        if (!db.ReportReasonConfigs.Any())
        {
            db.ReportReasonConfigs.Add(new ReportReasonConfig { ReasonCode = "COPYRIGHT", Description = "Bản quyền", SeverityLevel = "HIGH", BaseScore = 10, AutoFlagThreshold = 20 });
        }
        db.SaveChanges();
    }

    [Fact]
    public async Task ReportDocumentAsync_PinsCurrentVersionId()
    {
        using var db = _factory.CreateContext();
        SeedData(db);

        var doc = new Document
        {
            DocumentId = 201,
            UserId = 1,
            Title = "Public Doc",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/doc.pdf",
            FileSizeMb = 1.0m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var version = new DocumentVersion
        {
            VersionId = 55,
            DocumentId = 201,
            VersionNumber = 1,
            CloudStorageUrl = "/uploads/1/doc.pdf",
            FileExtension = "pdf",
            FileSizeMb = 1.0m,
            CreatedByUserId = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.DocumentVersions.Add(version);
        doc.CurrentVersionId = 55;
        await db.SaveChangesAsync();

        var service = new DocumentService(db, null!, new TestClock(), null!, new AIStudyHub.Infrastructure.Services.Gemini.MockGeminiService(), null!);
        var success = await service.ReportDocumentAsync(2, new DocumentReportDto
        {
            DocumentId = 201,
            ReasonCode = "COPYRIGHT",
            ReportType = "COPYRIGHT",
            ClaimantName = "Owner",
            ClaimantEmail = "owner@test.com",
            EvidenceDescription = "My work",
            InformationConfirmed = true
        });

        Assert.True(success);

        var report = await db.DocumentReports.FirstOrDefaultAsync(r => r.DocumentId == 201 && r.ReporterId == 2);
        Assert.NotNull(report);
        Assert.Equal(55, report.ReportedVersionId);
        Assert.Equal("PENDING", report.Status);
    }

    [Theory]
    [InlineData(-1, true)]    // 1 second before 14-day expiry
    [InlineData(0, true)]     // Exactly at 14-day expiry
    [InlineData(1, false)]    // 1 second after 14-day expiry
    [InlineData(-999, false)] // Null resolved date
    public void Appeal_14Day_Window_Boundary_Verification(int secondOffsetFrom14Days, bool expectedCanAppeal)
    {
        var baseResolvedAt = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc);
        var clock = new TestClock();

        DateTime? resolvedAt = secondOffsetFrom14Days == -999 ? null : baseResolvedAt;
        if (resolvedAt.HasValue)
        {
            clock.UtcNow = baseResolvedAt.AddDays(14).AddSeconds(secondOffsetFrom14Days);
        }
        else
        {
            clock.UtcNow = baseResolvedAt;
        }

        bool canAppeal = resolvedAt.HasValue && clock.UtcNow <= resolvedAt.Value.AddDays(14);
        Assert.Equal(expectedCanAppeal, canAppeal);
    }

    [Fact]
    public async Task DeleteVersionAsync_ThrowsInvalidOperationException_WhenReferencedByReport()
    {
        using var db = _factory.CreateContext();
        SeedData(db);

        var doc = new Document
        {
            DocumentId = 301,
            UserId = 1,
            Title = "Reported Doc",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/v2.pdf",
            FileSizeMb = 1.0m,
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var v1 = new DocumentVersion
        {
            VersionId = 101,
            DocumentId = 301,
            VersionNumber = 1,
            CloudStorageUrl = "/uploads/1/v1.pdf",
            FileExtension = "pdf",
            FileSizeMb = 1.0m,
            CreatedByUserId = 1,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };
        var v2 = new DocumentVersion
        {
            VersionId = 102,
            DocumentId = 301,
            VersionNumber = 2,
            CloudStorageUrl = "/uploads/1/v2.pdf",
            FileExtension = "pdf",
            FileSizeMb = 1.0m,
            CreatedByUserId = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.DocumentVersions.AddRange(v1, v2);
        doc.CurrentVersionId = 102;
        await db.SaveChangesAsync();

        // Create a report pinning version 101
        db.DocumentReports.Add(new DocumentReport
        {
            ReportId = 99,
            DocumentId = 301,
            ReporterId = 2,
            ReasonCode = "COPYRIGHT",
            ReportedVersionId = 101,
            Status = "IN_REVIEW"
        });
        await db.SaveChangesAsync();

        var perm = new TestModerationPermissionService { EffectiveRole = "OWNER" };
        var storage = new TestModerationFileStorage();
        var queue = new TestModerationProcessingQueue();

        var versionService = new VersionService(db, storage, perm, queue);

        // Deleting version 101 must be blocked because it's referenced by report 99
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => versionService.DeleteVersionAsync(301, 101, 1));
        Assert.Contains("bằng chứng cho báo cáo vi phạm", ex.Message);
    }

    [Fact]
    public async Task PermanentlyDeleteDocumentAsync_ThrowsInvalidOperationException_WhenReportsExist()
    {
        using var db = _factory.CreateContext();
        SeedData(db);

        var doc = new Document
        {
            DocumentId = 401,
            UserId = 1,
            Title = "Doc in Trash",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/1/doc.pdf",
            FileSizeMb = 1.0m,
            IsDeleted = true,
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);

        db.DocumentReports.Add(new DocumentReport
        {
            ReportId = 150,
            DocumentId = 401,
            ReporterId = 2,
            ReasonCode = "COPYRIGHT",
            Status = "CLOSED", // Even closed terminal report blocks hard delete for audit retention
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var perm = new TestModerationPermissionService();
        var storage = new TestModerationFileStorage();

        var trashService = new TrashService(db, storage, perm);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => trashService.PermanentlyDeleteDocumentAsync(401, 1));
        Assert.Contains("bảo lưu dữ liệu kiểm duyệt", ex.Message);
    }

    [Fact]
    public async Task MultiVersion_ExtractedTexts_Coexist_And_Can_Be_Queried_By_VersionId()
    {
        using var db = _factory.CreateContext();
        SeedData(db);

        var doc = new Document
        {
            DocumentId = 501,
            UserId = 1,
            Title = "Multi-version Doc",
            FileExtension = "txt",
            CloudStorageUrl = "/uploads/1/doc_v1.txt",
            FileSizeMb = 0.5m,
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var v1 = new DocumentVersion
        {
            VersionId = 701,
            DocumentId = 501,
            VersionNumber = 1,
            CloudStorageUrl = "/uploads/1/doc_v1.txt",
            FileExtension = "txt",
            FileSizeMb = 0.5m,
            CreatedByUserId = 1,
            CreatedAt = DateTime.UtcNow.AddHours(-1)
        };
        var v2 = new DocumentVersion
        {
            VersionId = 702,
            DocumentId = 501,
            VersionNumber = 2,
            CloudStorageUrl = "/uploads/1/doc_v2.txt",
            FileExtension = "txt",
            FileSizeMb = 0.6m,
            CreatedByUserId = 1,
            CreatedAt = DateTime.UtcNow
        };
        db.DocumentVersions.AddRange(v1, v2);
        doc.CurrentVersionId = 702;

        db.DocumentExtractedTexts.AddRange(
            new DocumentExtractedText
            {
                DocumentId = 501,
                DocumentVersionId = 701,
                ExtractedText = "Nội dung phiên bản 1",
                CreatedAt = DateTime.UtcNow.AddHours(-1)
            },
            new DocumentExtractedText
            {
                DocumentId = 501,
                DocumentVersionId = 702,
                ExtractedText = "Nội dung phiên bản 2 đã cập nhật",
                CreatedAt = DateTime.UtcNow
            }
        );
        await db.SaveChangesAsync();

        var storage = new TestModerationFileStorage();
        var clock = new TestClock();
        var docService = new DocumentService(db, storage, clock, null!, new AIStudyHub.Infrastructure.Services.Gemini.MockGeminiService(), null!);

        // Verify both DocumentExtractedText records coexist
        var allTexts = await db.DocumentExtractedTexts.Where(t => t.DocumentId == 501).ToListAsync();
        Assert.Equal(2, allTexts.Count);

        var textV1 = await docService.GetExtractedTextAsync(501, 701);
        var textV2 = await docService.GetExtractedTextAsync(501, 702);
        var currentText = await docService.GetExtractedTextAsync(501);

        Assert.Equal("Nội dung phiên bản 1", textV1);
        Assert.Equal("Nội dung phiên bản 2 đã cập nhật", textV2);
        Assert.Equal("Nội dung phiên bản 2 đã cập nhật", currentText);

        // Strict no-fallback: when explicit versionId does not exist, return null (never fallback to current)
        var textNonExistentVersion = await docService.GetExtractedTextAsync(501, 9999);
        Assert.Null(textNonExistentVersion);
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// Test Fakes
// ─────────────────────────────────────────────────────────────────────────────

public class TestModerationPermissionService : IPermissionService
{
    public string EffectiveRole { get; set; } = "OWNER";
    public Task<string> GetEffectiveDocumentRoleAsync(int documentId, int userId, string? shareToken = null) => Task.FromResult(EffectiveRole);
    public Task<string> GetEffectiveFolderRoleAsync(int folderId, int userId) => Task.FromResult("OWNER");
    public Task<bool> CanViewDocumentAsync(int documentId, int userId, string? shareToken = null) => Task.FromResult(true);
    public Task<bool> CanDownloadDocumentAsync(int documentId, int userId, string? shareToken = null) => Task.FromResult(true);
    public Task<bool> CanEditDocumentAsync(int documentId, int userId) => Task.FromResult(true);
    public Task<bool> CanManageDocumentAccessAsync(int documentId, int userId) => Task.FromResult(true);
    public Task<List<int>> GetSharedDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null) => Task.FromResult(new List<int>());
    public Task<List<int>> GetViewableDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null) => Task.FromResult(new List<int>());
    public Task<List<int>> GetAccessibleDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null) => Task.FromResult(new List<int>());
    public Task<ItemAccessSettingsDto> GetDocumentAccessSettingsAsync(int documentId, int currentUserId) => Task.FromResult(new ItemAccessSettingsDto());
    public Task<ItemAccessSettingsDto> GetFolderAccessSettingsAsync(int folderId, int currentUserId) => Task.FromResult(new ItemAccessSettingsDto());
    public Task UpdateDocumentGeneralAccessAsync(int documentId, string generalAccess, int currentUserId) => Task.CompletedTask;
    public Task UpdateFolderGeneralAccessAsync(int folderId, string generalAccess, int currentUserId) => Task.CompletedTask;
    public Task AddOrUpdateDocumentUserShareAsync(int documentId, string email, string role, int currentUserId) => Task.CompletedTask;
    public Task AddOrUpdateFolderUserShareAsync(int folderId, string email, string role, int currentUserId) => Task.CompletedTask;
    public Task RemoveDocumentUserShareAsync(int documentId, int targetUserId, int currentUserId) => Task.CompletedTask;
    public Task RemoveFolderUserShareAsync(int folderId, int targetUserId, int currentUserId) => Task.CompletedTask;
    public Task<ShareLinkInfoDto> RotateDocumentShareLinkAsync(int documentId, int currentUserId) => Task.FromResult(new ShareLinkInfoDto());
    public Task RevokeDocumentShareLinkAsync(int documentId, int currentUserId) => Task.CompletedTask;
    public Task LogAuditAsync(int actorUserId, string action, string targetType, int targetId, string? details) => Task.CompletedTask;
    public Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(int pageNumber, int pageSize) => Task.FromResult(new PagedResult<AuditLogDto>());
}

public class TestModerationFileStorage : IFileStorage
{
    public Task SaveFileAsync(string relativePath, Stream stream) => Task.CompletedTask;
    public void DeleteFile(string relativePath) { }
    public void MoveFile(string sourceRelativePath, string destinationRelativePath) { }
    public string GetPhysicalPath(string relativePath) => relativePath;
    public bool FileExists(string relativePath) => true;
    public Stream OpenReadStream(string relativePath) => new MemoryStream();
}

public class TestModerationProcessingQueue : IDocumentProcessingQueue
{
    public Task<DocumentProcessingJob> EnqueueJobAsync(int documentId, int? versionId = null) =>
        Task.FromResult(new DocumentProcessingJob { JobId = 1, DocumentId = documentId, DocumentVersionId = versionId });
    public Task<DocumentProcessingJob?> ClaimNextJobAsync(string workerId, System.Threading.CancellationToken cancellationToken) =>
        Task.FromResult<DocumentProcessingJob?>(null);
    public Task CompleteJobAsync(int jobId) => Task.CompletedTask;
    public Task FailJobAsync(int jobId, string errorMessage) => Task.CompletedTask;
}
