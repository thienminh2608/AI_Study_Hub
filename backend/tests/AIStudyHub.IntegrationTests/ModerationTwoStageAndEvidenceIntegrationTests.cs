using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using AIStudyHub.Api.Controllers;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudyHub.IntegrationTests;

public class ModerationTwoStageAndEvidenceIntegrationTests : IClassFixture<CustomWebApplicationFactory>, IDisposable
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ModerationTwoStageAndEvidenceIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public void Dispose()
    {
    }

    private async Task SeedBaseDataAsync()
    {
        await _factory.SeedDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        if (!await db.Users.AnyAsync(u => u.UserId == 10))
        {
            db.Users.AddRange(
                new User { UserId = 10, Username = "mod_alice", Email = "alice@mod.com", PasswordHash = "hash", Role = "MODERATOR", Status = "ACTIVE", TierId = 1 },
                new User { UserId = 11, Username = "mod_bob", Email = "bob@mod.com", PasswordHash = "hash", Role = "MODERATOR", Status = "ACTIVE", TierId = 1 },
                new User { UserId = 12, Username = "doc_author", Email = "author@test.com", PasswordHash = "hash", Role = "STUDENT", Status = "ACTIVE", TierId = 1 },
                new User { UserId = 13, Username = "reporter_user", Email = "reporter@test.com", PasswordHash = "hash", Role = "STUDENT", Status = "ACTIVE", TierId = 1 }
            );
            await db.SaveChangesAsync();
        }

        if (!await db.ReportReasonConfigs.AnyAsync(r => r.ReasonCode == "COPYRIGHT"))
        {
            db.ReportReasonConfigs.Add(new ReportReasonConfig
            {
                ReasonCode = "COPYRIGHT",
                Description = "Vi phạm bản quyền",
                SeverityLevel = "HIGH",
                BaseScore = 10,
                AutoFlagThreshold = 20
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task MultiVersion_ExtractedTexts_Cardinality_And_Isolation_Test()
    {
        await SeedBaseDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        int docId = 1001;
        var doc = new Document
        {
            DocumentId = docId,
            UserId = 12,
            Title = "Multi-Version Document",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/12/doc_v2.pdf",
            FileSizeMb = 1.2m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var v1 = new DocumentVersion
        {
            VersionId = 2001,
            DocumentId = docId,
            VersionNumber = 1,
            CloudStorageUrl = "/uploads/12/doc_v1.pdf",
            FileExtension = "pdf",
            FileSizeMb = 1.0m,
            CreatedByUserId = 12,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        };
        var v2 = new DocumentVersion
        {
            VersionId = 2002,
            DocumentId = docId,
            VersionNumber = 2,
            CloudStorageUrl = "/uploads/12/doc_v2.pdf",
            FileExtension = "pdf",
            FileSizeMb = 1.2m,
            CreatedByUserId = 12,
            CreatedAt = DateTime.UtcNow
        };
        db.DocumentVersions.AddRange(v1, v2);
        doc.CurrentVersionId = 2002;
        await db.SaveChangesAsync();

        // Add 2 distinct version extracted texts
        db.DocumentExtractedTexts.AddRange(
            new DocumentExtractedText
            {
                DocumentId = docId,
                DocumentVersionId = 2001,
                ExtractedText = "Nội dung trích xuất của phiên bản 1 (chứa vi phạm)",
                CreatedAt = DateTime.UtcNow.AddDays(-2)
            },
            new DocumentExtractedText
            {
                DocumentId = docId,
                DocumentVersionId = 2002,
                ExtractedText = "Nội dung trích xuất của phiên bản 2 (đã gỡ vi phạm)",
                CreatedAt = DateTime.UtcNow
            }
        );

        // Report is pinned to Version 1 (2001)
        int reportId = 5001;
        db.DocumentReports.Add(new DocumentReport
        {
            ReportId = reportId,
            DocumentId = docId,
            ReporterId = 13,
            ReasonCode = "COPYRIGHT",
            ReportedVersionId = 2001,
            Status = "IN_REVIEW",
            AssignedModeratorId = 10,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        // Act: Moderator Alice queries text evidence
        var token = _factory.GenerateJwtToken(10, "mod_alice", "MODERATOR");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.GetAsync($"/api/moderation/reports/{reportId}/evidence/text");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(docId, json.GetProperty("documentId").GetInt32());
        Assert.Equal(2001, json.GetProperty("versionId").GetInt32());
        Assert.Equal("Nội dung trích xuất của phiên bản 1 (chứa vi phạm)", json.GetProperty("extractedText").GetString());
        Assert.True(json.GetProperty("isVersionPinned").GetBoolean());
        Assert.False(json.GetProperty("isLegacyFallback").GetBoolean());
    }

    [Fact]
    public async Task Strict_No_Fallback_When_Reported_Version_Text_Missing()
    {
        await SeedBaseDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        int docId = 1002;
        var doc = new Document
        {
            DocumentId = docId,
            UserId = 12,
            Title = "Doc Missing V1 Text",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/12/doc_v2.pdf",
            FileSizeMb = 1.0m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var v1 = new DocumentVersion
        {
            VersionId = 2003,
            DocumentId = docId,
            VersionNumber = 1,
            CloudStorageUrl = "/uploads/12/doc_v1.pdf",
            FileExtension = "pdf",
            FileSizeMb = 1.0m,
            CreatedByUserId = 12,
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };
        var v2 = new DocumentVersion
        {
            VersionId = 2004,
            DocumentId = docId,
            VersionNumber = 2,
            CloudStorageUrl = "/uploads/12/doc_v2.pdf",
            FileExtension = "pdf",
            FileSizeMb = 1.0m,
            CreatedByUserId = 12,
            CreatedAt = DateTime.UtcNow
        };
        db.DocumentVersions.AddRange(v1, v2);
        doc.CurrentVersionId = 2004;

        // ONLY Version 2 has extracted text
        db.DocumentExtractedTexts.Add(new DocumentExtractedText
        {
            DocumentId = docId,
            DocumentVersionId = 2004,
            ExtractedText = "Text của version 2",
            CreatedAt = DateTime.UtcNow
        });

        // Report pinned to Version 1 (2003)
        int reportId = 5002;
        db.DocumentReports.Add(new DocumentReport
        {
            ReportId = reportId,
            DocumentId = docId,
            ReporterId = 13,
            ReasonCode = "COPYRIGHT",
            ReportedVersionId = 2003,
            Status = "IN_REVIEW",
            AssignedModeratorId = 10,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var token = _factory.GenerateJwtToken(10, "mod_alice", "MODERATOR");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act: Must return 404 Not Found and NOT fallback to version 2 text
        var response = await _client.GetAsync($"/api/moderation/reports/{reportId}/evidence/text");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("isVersionPinned").GetBoolean());
        Assert.False(json.GetProperty("isLegacyFallback").GetBoolean());
    }

    [Fact]
    public async Task Evidence_Raw_Stream_Endpoint_Test()
    {
        await SeedBaseDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        // Create a real physical file on disk via storage abstraction
        string relPath = "uploads/12/evidence_sample.pdf";
        byte[] expectedBytes = "PDF_MOCK_CONTENT_EVIDENCE_TEST"u8.ToArray();
        using (var ms = new MemoryStream(expectedBytes))
        {
            await storage.SaveFileAsync(relPath, ms);
        }

        int docId = 1003;
        var doc = new Document
        {
            DocumentId = docId,
            UserId = 12,
            Title = "Sample Evidence Document",
            FileExtension = "pdf",
            CloudStorageUrl = $"/{relPath}",
            FileSizeMb = 0.1m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        var v1 = new DocumentVersion
        {
            VersionId = 2005,
            DocumentId = docId,
            VersionNumber = 1,
            CloudStorageUrl = $"/{relPath}",
            FileExtension = "pdf",
            FileSizeMb = 0.1m,
            CreatedByUserId = 12,
            CreatedAt = DateTime.UtcNow
        };
        db.DocumentVersions.Add(v1);
        doc.CurrentVersionId = 2005;

        int reportId = 5003;
        db.DocumentReports.Add(new DocumentReport
        {
            ReportId = reportId,
            DocumentId = docId,
            ReporterId = 13,
            ReasonCode = "COPYRIGHT",
            ReportedVersionId = 2005,
            Status = "IN_REVIEW",
            AssignedModeratorId = 10,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var token = _factory.GenerateJwtToken(10, "mod_alice", "MODERATOR");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act: Fetch raw evidence stream
        var response = await _client.GetAsync($"/api/moderation/reports/{reportId}/evidence/raw");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);

        // Verify Content-Disposition download filename
        var contentDisposition = response.Content.Headers.ContentDisposition;
        Assert.NotNull(contentDisposition);
        Assert.Contains($"evidence_report_{reportId}_v1_", contentDisposition.FileName ?? contentDisposition.FileNameStar);

        var downloadedBytes = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(expectedBytes, downloadedBytes);

        // Case 2: When physical file is missing, return 404
        int docMissingFileId = 1005;
        db.Documents.Add(new Document
        {
            DocumentId = docMissingFileId,
            UserId = 12,
            Title = "Missing File Document",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/non_existent_file.pdf",
            FileSizeMb = 1.0m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        int reportMissingFileId = 5004;
        db.DocumentReports.Add(new DocumentReport
        {
            ReportId = reportMissingFileId,
            DocumentId = docMissingFileId,
            ReporterId = 13,
            ReasonCode = "COPYRIGHT",
            ReportedVersion = new DocumentVersion
            {
                DocumentId = docMissingFileId,
                VersionNumber = 99,
                CloudStorageUrl = "/uploads/non_existent_file.pdf",
                FileExtension = "pdf",
                FileSizeMb = 1.0m,
                CreatedByUserId = 12
            },
            Status = "IN_REVIEW",
            AssignedModeratorId = 10,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var notFoundResponse = await _client.GetAsync($"/api/moderation/reports/{reportMissingFileId}/evidence/raw");
        Assert.Equal(HttpStatusCode.NotFound, notFoundResponse.StatusCode);
    }

    [Fact]
    public async Task Truly_Concurrent_Claim_Lock_Test()
    {
        await SeedBaseDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        int docId = 1004;
        db.Documents.Add(new Document
        {
            DocumentId = docId,
            UserId = 12,
            Title = "Contested Document Concurrent",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/12/doc.pdf",
            FileSizeMb = 1.0m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        int reportId = 5005;
        db.DocumentReports.Add(new DocumentReport
        {
            ReportId = reportId,
            DocumentId = docId,
            ReporterId = 13,
            ReasonCode = "COPYRIGHT",
            Status = "PENDING",
            AssignedModeratorId = null,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var aliceToken = _factory.GenerateJwtToken(10, "mod_alice", "MODERATOR");
        var bobToken = _factory.GenerateJwtToken(11, "mod_bob", "MODERATOR");

        using var clientAlice = _factory.CreateClient();
        clientAlice.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);

        using var clientBob = _factory.CreateClient();
        clientBob.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);

        // Act: Truly concurrent claim attempt via Task.WhenAll
        var taskAlice = clientAlice.PostAsync($"/api/moderation/reports/{reportId}/assign", null);
        var taskBob = clientBob.PostAsync($"/api/moderation/reports/{reportId}/assign", null);

        var responses = await Task.WhenAll(taskAlice, taskBob);

        // Exactly one request must succeed (200 OK) and the other must fail (409 Conflict)
        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.OK, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        // Verify that in database, assigned_moderator_id is set to the winner and status is IN_REVIEW
        using var checkScope = _factory.Services.CreateScope();
        var checkDb = checkScope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
        var r = await checkDb.DocumentReports.FindAsync(reportId);
        Assert.NotNull(r);
        Assert.Equal("IN_REVIEW", r.Status);
        Assert.True(r.AssignedModeratorId == 10 || r.AssignedModeratorId == 11);
    }

    [Fact]
    public async Task Moderator_Reporter_Cannot_Handle_Own_Report_And_Does_Not_Receive_Its_Notice()
    {
        await SeedBaseDataAsync();
        const int docId = 1014;

        using (var seedScope = _factory.Services.CreateScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
            db.Documents.Add(new Document
            {
                DocumentId = docId,
                UserId = 12,
                Title = "Document Reported By Moderator",
                FileExtension = "pdf",
                CloudStorageUrl = "/uploads/12/mod-reported.pdf",
                FileSizeMb = 1.0m,
                SharingPermission = "PUBLIC",
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var aliceToken = _factory.GenerateJwtToken(10, "mod_alice", "MODERATOR");
        var bobToken = _factory.GenerateJwtToken(11, "mod_bob", "MODERATOR");
        using var aliceClient = _factory.CreateClient();
        aliceClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);
        using var bobClient = _factory.CreateClient();
        bobClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);

        var reportResponse = await aliceClient.PostAsJsonAsync("/api/document/report", new DocumentReportDto
        {
            DocumentId = docId,
            ReasonCode = "COPYRIGHT",
            ReportType = "COMMUNITY",
            AdditionalDetails = "Moderator phát hiện nội dung có dấu hiệu vi phạm."
        });
        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);

        int reportId;
        using (var checkScope = _factory.Services.CreateScope())
        {
            var db = checkScope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
            var report = await db.DocumentReports.SingleAsync(r => r.DocumentId == docId && r.ReporterId == 10);
            reportId = report.ReportId;
            Assert.False(await db.ModerationNotices.AnyAsync(n => n.ReportId == reportId && n.UserId == 10));
            Assert.True(await db.ModerationNotices.AnyAsync(n => n.ReportId == reportId && n.UserId == 11));
        }

        var selfAssignResponse = await aliceClient.PostAsync($"/api/moderation/reports/{reportId}/assign", null);
        Assert.Equal(HttpStatusCode.Conflict, selfAssignResponse.StatusCode);

        var independentAssignResponse = await bobClient.PostAsync($"/api/moderation/reports/{reportId}/assign", null);
        Assert.Equal(HttpStatusCode.OK, independentAssignResponse.StatusCode);

        // Protect legacy or manually corrupted assignments as well: a reporter must
        // still be rejected at decision time even if assigned_moderator_id points to them.
        using (var legacyScope = _factory.Services.CreateScope())
        {
            var db = legacyScope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
            await db.DocumentReports
                .Where(r => r.ReportId == reportId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(r => r.AssignedModeratorId, 10)
                    .SetProperty(r => r.Status, "IN_REVIEW"));
        }

        var selfResolveResponse = await aliceClient.PostAsJsonAsync(
            $"/api/moderation/reports/{reportId}/confirm-violation",
            new ModerationDecisionDto { Note = "Không được phép tự xử lý báo cáo." });
        Assert.Equal(HttpStatusCode.Forbidden, selfResolveResponse.StatusCode);
    }

    [Fact]
    public async Task Two_Stage_Resolution_Authorization_Test()
    {
        await SeedBaseDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        int docId = 1006;
        db.Documents.Add(new Document
        {
            DocumentId = docId,
            UserId = 12,
            Title = "Doc for Resolution Auth",
            FileExtension = "pdf",
            CloudStorageUrl = "/uploads/12/doc.pdf",
            FileSizeMb = 1.0m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow
        });

        int reportId = 5006;
        db.DocumentReports.Add(new DocumentReport
        {
            ReportId = reportId,
            DocumentId = docId,
            ReporterId = 13,
            ReasonCode = "COPYRIGHT",
            Status = "IN_REVIEW",
            AssignedModeratorId = 10, // Assigned to Alice
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var bobToken = _factory.GenerateJwtToken(11, "mod_bob", "MODERATOR");
        var aliceToken = _factory.GenerateJwtToken(10, "mod_alice", "MODERATOR");

        // Moderator Bob tries to resolve Alice's report -> 403 Forbidden
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bobToken);
        var resolveResponseBob = await _client.PostAsJsonAsync($"/api/moderation/reports/{reportId}/confirm-violation", new ModerationDecisionDto
        {
            Note = "Bob trying to resolve Alice's case"
        });
        Assert.Equal(HttpStatusCode.Forbidden, resolveResponseBob.StatusCode);

        // Moderator Alice resolves her report -> 200 OK
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", aliceToken);
        var resolveResponseAlice = await _client.PostAsJsonAsync($"/api/moderation/reports/{reportId}/confirm-violation", new ModerationDecisionDto
        {
            Note = "Vi phạm bản quyền rõ ràng."
        });
        Assert.Equal(HttpStatusCode.OK, resolveResponseAlice.StatusCode);

        using var finalScope = _factory.Services.CreateScope();
        var finalDb = finalScope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
        var resolvedReport = await finalDb.DocumentReports.Include(r => r.Document).FirstOrDefaultAsync(r => r.ReportId == reportId);
        Assert.Equal("VIOLATION_CONFIRMED", resolvedReport!.Status);
        Assert.NotNull(resolvedReport.ResolvedAt);
        Assert.Equal("HIDDEN", resolvedReport.Document.ModerationStatus);
        Assert.True(resolvedReport.Document.IsFlagged);
    }

    [Fact]
    public async Task Truly_Concurrent_Extraction_Test()
    {
        await SeedBaseDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        int docId = 1007;
        var doc = new Document
        {
            DocumentId = docId,
            UserId = 12,
            Title = "Concurrent Extraction Doc",
            FileExtension = "txt",
            CloudStorageUrl = "/uploads/12/conc_doc.txt",
            FileSizeMb = 0.5m,
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(doc);

        var v1 = new DocumentVersion
        {
            VersionId = 2007,
            DocumentId = docId,
            VersionNumber = 1,
            CloudStorageUrl = "/uploads/12/conc_doc.txt",
            FileExtension = "txt",
            FileSizeMb = 0.5m,
            CreatedByUserId = 12,
            CreatedAt = DateTime.UtcNow
        };
        db.DocumentVersions.Add(v1);
        doc.CurrentVersionId = 2007;
        await db.SaveChangesAsync();

        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        string relPath = "uploads/12/conc_doc.txt";
        using (var ms = new MemoryStream("Nội dung trích xuất đồng thời"u8.ToArray()))
        {
            await storage.SaveFileAsync(relPath, ms);
        }

        // Run 2 parallel extractions for the same document and version in separate DbContext instances
        var task1 = Task.Run(async () =>
        {
            using var s1 = _factory.Services.CreateScope();
            var docService1 = s1.ServiceProvider.GetRequiredService<IDocumentService>();
            await docService1.ProcessExtractionAsync(docId, 2007);
        });

        var task2 = Task.Run(async () =>
        {
            using var s2 = _factory.Services.CreateScope();
            var docService2 = s2.ServiceProvider.GetRequiredService<IDocumentService>();
            await docService2.ProcessExtractionAsync(docId, 2007);
        });

        await Task.WhenAll(task1, task2);

        // Verify exactly 1 row exists for (docId, 2007) and not duplicated
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
        var rows = await verifyDb.DocumentExtractedTexts.Where(t => t.DocumentId == docId && t.DocumentVersionId == 2007).ToListAsync();
        Assert.Single(rows);

        // Verify chunks exist and are not duplicated
        var chunks = await verifyDb.DocumentChunks.Where(c => c.DocumentId == docId && c.DocumentVersionId == 2007).ToListAsync();
        Assert.NotEmpty(chunks);

        // Verify doc is READY
        var updatedDoc = await verifyDb.Documents.FindAsync(docId);
        Assert.NotNull(updatedDoc);
        Assert.Equal("READY", updatedDoc.AiParsingStatus);

        // Verify moderation notice was added
        var notices = await verifyDb.ModerationNotices.Where(n => n.DocumentId == docId && n.Type == "DOCUMENT_AI_READY").ToListAsync();
        Assert.NotEmpty(notices);
    }

    [Fact]
    public async Task Moderation_Paged_Queues_Coverage_Test()
    {
        await SeedBaseDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        // Seed 10 documents for review queue
        for (int i = 1; i <= 10; i++)
        {
            int dId = 1200 + i;
            db.Documents.Add(new Document
            {
                DocumentId = dId,
                UserId = 12,
                Title = $"Queue Doc {i:D2}",
                FileExtension = "pdf",
                CloudStorageUrl = $"/uploads/12/q_{i}.pdf",
                FileSizeMb = 1.0m,
                ModerationStatus = "PENDING_REVIEW",
                ModerationSubmittedAt = DateTime.UtcNow.AddMinutes(i),
                CreatedAt = DateTime.UtcNow
            });
        }

        // Seed 5 moderation actions for history
        for (int i = 1; i <= 5; i++)
        {
            db.ModerationActions.Add(new ModerationAction
            {
                ActionId = 7000 + i,
                ActorUserId = 10,
                DocumentId = 1201,
                Action = "APPROVE",
                PreviousStatus = "PENDING_REVIEW",
                NewStatus = "APPROVED",
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }
        await db.SaveChangesAsync();

        var token = _factory.GenerateJwtToken(10, "mod_alice", "MODERATOR");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // 1. Test /queue/paged
        var queueResp = await _client.GetAsync("/api/moderation/queue/paged?page=1&pageSize=4");
        Assert.Equal(HttpStatusCode.OK, queueResp.StatusCode);
        var qJson = await queueResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, qJson.GetProperty("items").EnumerateArray().Count());
        Assert.True(qJson.GetProperty("totalCount").GetInt32() >= 10);
        Assert.Equal(1, qJson.GetProperty("pageNumber").GetInt32());

        // 2. Test /reports/paged with deep validation
        var reportResp = await _client.GetAsync("/api/moderation/reports/paged?page=1&pageSize=3");
        Assert.Equal(HttpStatusCode.OK, reportResp.StatusCode);
        var rJson = await reportResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(rJson.GetProperty("items").EnumerateArray().Count() > 0);
        Assert.True(rJson.GetProperty("totalCount").GetInt32() > 0);
        Assert.Equal(1, rJson.GetProperty("pageNumber").GetInt32());
        Assert.Equal(3, rJson.GetProperty("pageSize").GetInt32());

        // 3. Test /appeals/paged with seeded appeal
        int pagedReportId = 6999;
        db.DocumentReports.Add(new DocumentReport
        {
            ReportId = pagedReportId,
            DocumentId = 1201,
            ReporterId = 13,
            ReasonCode = "COPYRIGHT",
            Status = "APPEALED",
            CreatedAt = DateTime.UtcNow
        });
        db.ModerationAppeals.Add(new ModerationAppeal
        {
            AppealId = 8801,
            ReportId = pagedReportId,
            SubmittedByUserId = 12,
            Explanation = "Tài liệu này không vi phạm.",
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var appealResp = await _client.GetAsync("/api/moderation/appeals/paged?page=1&pageSize=5");
        Assert.Equal(HttpStatusCode.OK, appealResp.StatusCode);
        var aJson = await appealResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(aJson.GetProperty("items").EnumerateArray().Count() >= 1);
        Assert.True(aJson.GetProperty("totalCount").GetInt32() >= 1);

        // 4. Test /history/paged
        var historyResp = await _client.GetAsync("/api/moderation/history/paged?page=1&pageSize=3");
        Assert.Equal(HttpStatusCode.OK, historyResp.StatusCode);
        var hJson = await historyResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, hJson.GetProperty("items").EnumerateArray().Count());
        Assert.True(hJson.GetProperty("totalCount").GetInt32() >= 5);
        Assert.Equal(1, hJson.GetProperty("pageNumber").GetInt32());
        Assert.Equal(3, hJson.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task ReplaceDocumentAsync_Creates_New_Version_And_Preserves_Historical_Evidence()
    {
        await SeedBaseDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var docService = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        // 1. Setup old document with version 1
        int duplicateDocId = 1008;
        string oldFilePath = "uploads/12/original_doc.pdf";
        using (var ms1 = new MemoryStream("OLD_PDF_VERSION_1_BYTES"u8.ToArray()))
        {
            await storage.SaveFileAsync(oldFilePath, ms1);
        }

        var oldDoc = new Document
        {
            DocumentId = duplicateDocId,
            UserId = 12,
            Title = "Duplicate Replacement Test",
            FileExtension = "pdf",
            CloudStorageUrl = $"/{oldFilePath}",
            FileSizeMb = 1.0m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };
        db.Documents.Add(oldDoc);

        var v1 = new DocumentVersion
        {
            DocumentId = duplicateDocId,
            VersionNumber = 1,
            CloudStorageUrl = $"/{oldFilePath}",
            FileExtension = "pdf",
            FileSizeMb = 1.0m,
            CreatedByUserId = 12,
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        };
        db.DocumentVersions.Add(v1);
        await db.SaveChangesAsync();

        int v1Id = v1.VersionId;
        oldDoc.CurrentVersionId = v1Id;

        db.DocumentExtractedTexts.Add(new DocumentExtractedText
        {
            DocumentId = duplicateDocId,
            DocumentVersionId = v1Id,
            ExtractedText = "Bằng chứng văn bản phiên bản 1 (cần bảo tồn)",
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        });

        db.DocumentChunks.Add(new DocumentChunk
        {
            DocumentId = duplicateDocId,
            DocumentVersionId = v1Id,
            ChunkIndex = 0,
            Text = "Chunk version 1",
            CreatedAt = DateTime.UtcNow.AddDays(-5)
        });

        // Add report pinning Version 1
        int reportId = 5008;
        db.DocumentReports.Add(new DocumentReport
        {
            ReportId = reportId,
            DocumentId = duplicateDocId,
            ReporterId = 13,
            ReasonCode = "COPYRIGHT",
            ReportedVersionId = v1Id,
            Status = "IN_REVIEW",
            AssignedModeratorId = 10,
            CreatedAt = DateTime.UtcNow.AddDays(-2)
        });

        // 2. Setup pending document with new file and new extracted text/chunk
        int pendingDocId = 1009;
        string pendingFilePath = "uploads/12/temp_pending_doc.pdf";
        using (var ms2 = new MemoryStream("NEW_PDF_VERSION_2_BYTES"u8.ToArray()))
        {
            await storage.SaveFileAsync(pendingFilePath, ms2);
        }

        var pendingDoc = new Document
        {
            DocumentId = pendingDocId,
            UserId = 12,
            Title = "Duplicate Replacement Test",
            FileExtension = "pdf",
            CloudStorageUrl = $"/{pendingFilePath}",
            FileSizeMb = 1.5m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(pendingDoc);

        db.DocumentExtractedTexts.Add(new DocumentExtractedText
        {
            DocumentId = pendingDocId,
            DocumentVersionId = null,
            ExtractedText = "Văn bản mới đã sửa đổi (phiên bản 2)",
            CreatedAt = DateTime.UtcNow
        });

        db.DocumentChunks.Add(new DocumentChunk
        {
            DocumentId = pendingDocId,
            DocumentVersionId = null,
            ChunkIndex = 0,
            Text = "Chunk version 2",
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        // 3. Act: ReplaceDocumentAsync
        var result = await docService.ReplaceDocumentAsync(12, pendingDocId, duplicateDocId, "Duplicate Replacement Test", "Toán", "PUBLIC", null);
        Assert.NotNull(result);

        // 4. Assert Forensic Invariance:
        // A. Old physical file still exists on storage
        Assert.True(storage.FileExists(oldFilePath));

        // B. Old Version 1 still exists in database
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var allVersions = await verifyDb.DocumentVersions
            .Where(v => v.DocumentId == duplicateDocId)
            .OrderBy(v => v.VersionNumber)
            .ToListAsync();
        Assert.Equal(2, allVersions.Count);
        Assert.Equal(1, allVersions[0].VersionNumber);
        Assert.Equal(2, allVersions[1].VersionNumber);

        // C. Both Extracted Texts coexist
        var allTexts = await verifyDb.DocumentExtractedTexts
            .Where(t => t.DocumentId == duplicateDocId)
            .OrderBy(t => t.DocumentVersionId)
            .ToListAsync();
        Assert.Equal(2, allTexts.Count);
        Assert.Equal("Bằng chứng văn bản phiên bản 1 (cần bảo tồn)", allTexts[0].ExtractedText);
        Assert.Equal("Văn bản mới đã sửa đổi (phiên bản 2)", allTexts[1].ExtractedText);

        // D. Both Chunks coexist with proper Version pinning
        var allChunks = await verifyDb.DocumentChunks
            .Where(c => c.DocumentId == duplicateDocId)
            .OrderBy(c => c.DocumentVersionId)
            .ToListAsync();
        Assert.Equal(2, allChunks.Count);
        Assert.Equal(v1Id, allChunks[0].DocumentVersionId);
        Assert.Equal(allVersions[1].VersionId, allChunks[1].DocumentVersionId);

        // E. Report query on /evidence/text still returns Version 1 text
        var token = _factory.GenerateJwtToken(10, "mod_alice", "MODERATOR");
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var evidenceResp = await _client.GetAsync($"/api/moderation/reports/{reportId}/evidence/text");
        Assert.Equal(HttpStatusCode.OK, evidenceResp.StatusCode);
        var evJson = await evidenceResp.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Bằng chứng văn bản phiên bản 1 (cần bảo tồn)", evJson.GetProperty("extractedText").GetString());
        Assert.Equal(v1Id, evJson.GetProperty("versionId").GetInt32());
    }

    [Fact]
    public async Task Truly_Concurrent_Baseline_Version_Creation_Test()
    {
        await SeedBaseDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();

        int docId = 1010;
        string relPath = "uploads/12/baseline_race_doc.txt";
        using (var ms = new MemoryStream("Baseline race document text"u8.ToArray()))
        {
            await storage.SaveFileAsync(relPath, ms);
        }

        var doc = new Document
        {
            DocumentId = docId,
            UserId = 12,
            Title = "Baseline Race Doc",
            FileExtension = "txt",
            CloudStorageUrl = $"/{relPath}",
            FileSizeMb = 0.2m,
            CreatedAt = DateTime.UtcNow
            // CurrentVersionId is intentionally null! No versions exist!
        };
        db.Documents.Add(doc);
        await db.SaveChangesAsync();

        // Act: Run 2 parallel extractions concurrently from separate scopes with NO version ID specified
        var task1 = Task.Run(async () =>
        {
            using var s1 = _factory.Services.CreateScope();
            var docService1 = s1.ServiceProvider.GetRequiredService<IDocumentService>();
            await docService1.ProcessExtractionAsync(docId, null);
        });

        var task2 = Task.Run(async () =>
        {
            using var s2 = _factory.Services.CreateScope();
            var docService2 = s2.ServiceProvider.GetRequiredService<IDocumentService>();
            await docService2.ProcessExtractionAsync(docId, null);
        });

        await Task.WhenAll(task1, task2);

        // Assert: Exactly 1 baseline Version 1 and 1 DocumentExtractedText created
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var versions = await verifyDb.DocumentVersions.Where(v => v.DocumentId == docId).ToListAsync();
        Assert.Single(versions);
        Assert.Equal(1, versions[0].VersionNumber);

        var texts = await verifyDb.DocumentExtractedTexts.Where(t => t.DocumentId == docId).ToListAsync();
        Assert.Single(texts);
        Assert.Equal(versions[0].VersionId, texts[0].DocumentVersionId);
    }

    [Fact]
    public async Task ReplaceDocumentAsync_Compensates_Filesystem_On_Database_Failure()
    {
        await SeedBaseDataAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
        var storage = scope.ServiceProvider.GetRequiredService<IFileStorage>();
        var docService = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        int duplicateDocId = 1018;
        string oldFilePath = "uploads/12/existing_stable_doc.pdf";
        using (var ms1 = new MemoryStream("EXISTING_BYTES"u8.ToArray()))
        {
            await storage.SaveFileAsync(oldFilePath, ms1);
        }

        var oldDoc = new Document
        {
            DocumentId = duplicateDocId,
            UserId = 12,
            Title = "Stable Existing Doc",
            FileExtension = "pdf",
            CloudStorageUrl = $"/{oldFilePath}",
            FileSizeMb = 1.0m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(oldDoc);
        var v1 = new DocumentVersion
        {
            DocumentId = duplicateDocId,
            VersionNumber = 1,
            CloudStorageUrl = $"/{oldFilePath}",
            FileExtension = "pdf",
            FileSizeMb = 1.0m,
            CreatedByUserId = 12,
            CreatedAt = DateTime.UtcNow
        };
        db.DocumentVersions.Add(v1);
        await db.SaveChangesAsync();
        oldDoc.CurrentVersionId = v1.VersionId;

        int pendingDocId = 1019;
        string pendingFilePath = "uploads/12/temp_pending_to_compensate.pdf";
        using (var ms2 = new MemoryStream("PENDING_BYTES_TO_COMPENSATE"u8.ToArray()))
        {
            await storage.SaveFileAsync(pendingFilePath, ms2);
        }

        var pendingDoc = new Document
        {
            DocumentId = pendingDocId,
            UserId = 12,
            Title = "Compensate Source",
            FileExtension = "pdf",
            CloudStorageUrl = $"/{pendingFilePath}",
            FileSizeMb = 1.5m,
            SharingPermission = "PUBLIC",
            CreatedAt = DateTime.UtcNow
        };
        db.Documents.Add(pendingDoc);
        await db.SaveChangesAsync();

        // Inject a conflicting version entity in the tracker to cause DbUpdateException when SaveChangesAsync is called
        // AFTER RenamePhysicalFile has already moved the physical file to "uploads/12/Compensate_Target_Doc.pdf"
        db.DocumentVersions.Add(new DocumentVersion
        {
            DocumentId = duplicateDocId,
            VersionNumber = 1, // Duplicate VersionNumber 1 violates UQ_document_versions_document_id_version_number
            CloudStorageUrl = "/dummy.pdf",
            FileExtension = "pdf",
            FileSizeMb = 1.0m,
            CreatedByUserId = 12,
            CreatedAt = DateTime.UtcNow
        });

        // Act: ReplaceDocumentAsync will rename the physical file, then SaveChangesAsync will fail on unique constraint
        await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await docService.ReplaceDocumentAsync(12, pendingDocId, duplicateDocId, "Compensate Target Doc", "Toán", "PUBLIC", null);
        });

        // Assert Compensation:
        // 1. The pending file was reverted back to its original pending path
        Assert.True(storage.FileExists(pendingFilePath));
        // 2. The target renamed path no longer exists (has been moved back)
        Assert.False(storage.FileExists("uploads/12/Compensate_Target_Doc.pdf"));
    }
}
