using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudyHub.IntegrationTests;

public class ChatMessageCitationIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public ChatMessageCitationIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task SeedBaseDataAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();
        db.Database.EnsureCreated();

        if (!db.Subscriptions.Any())
        {
            db.Subscriptions.Add(new Subscription
            {
                TierId = 1,
                TierName = "Free",
                Price = 0,
                MaxStorageMb = 50,
                TotalStorageMb = 50,
                AiPromptLimitPerDay = 5
            });
            await db.SaveChangesAsync();
        }

        if (!db.Users.Any(u => u.UserId == 800))
        {
            db.Users.Add(new User
            {
                UserId = 800,
                Username = "chatUserA",
                PasswordHash = "hash",
                Email = "userA@test.com",
                Role = "STUDENT",
                TierId = 1
            });
        }
        if (!db.Users.Any(u => u.UserId == 801))
        {
            db.Users.Add(new User
            {
                UserId = 801,
                Username = "chatUserB",
                PasswordHash = "hash",
                Email = "userB@test.com",
                Role = "STUDENT",
                TierId = 1
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetSessionMessages_Returns_Persisted_Citations_And_Enforces_Session_Ownership()
    {
        await SeedBaseDataAsync();

        int sessionId = 8001;
        int docId = 8101;
        int versionId = 8201;
        int chunkId = 8301;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();

            var doc = new Document
            {
                DocumentId = docId,
                UserId = 800,
                Title = "Kiến trúc hệ thống phần mềm",
                FileExtension = "pdf",
                CloudStorageUrl = "/u/doc8101.pdf",
                SharingPermission = "PRIVATE"
            };
            var version = new DocumentVersion
            {
                VersionId = versionId,
                DocumentId = docId,
                VersionNumber = 1,
                CloudStorageUrl = "/u/doc8101_v1.pdf",
                FileExtension = "pdf",
                CreatedByUserId = 800
            };
            var chunk = new DocumentChunk
            {
                ChunkId = chunkId,
                DocumentId = docId,
                DocumentVersionId = versionId,
                ChunkIndex = 0,
                PageNumber = 5,
                StartOffset = 20,
                EndOffset = 220,
                HeadingPath = "Chương 2 > Thiết kế CSDL",
                Text = "Cơ sở dữ liệu của hệ thống được chuẩn hóa theo dạng 3NF."
            };

            var session = new ChatSession
            {
                SessionId = sessionId,
                UserId = 800,
                SessionName = "Phiên hỏi đáp môn Kiến trúc",
                AttachedDocumentId = docId
            };
            var message = new ChatMessage
            {
                SessionId = sessionId,
                Sender = "BOT",
                MessageContent = "Theo tài liệu, CSDL được chuẩn hóa dạng 3NF. [CHUNK:8301]",
                Display = true,
                CreatedAt = DateTime.UtcNow
            };

            var citation = new ChatMessageCitation
            {
                Message = message,
                DocumentId = docId,
                DocumentVersionId = versionId,
                ChunkId = chunkId,
                DocumentTitleSnapshot = "Kiến trúc hệ thống phần mềm",
                VersionNumberSnapshot = 1,
                FileExtensionSnapshot = "pdf",
                PageNumberSnapshot = 5,
                StartOffsetSnapshot = 20,
                EndOffsetSnapshot = 220,
                HeadingPathSnapshot = "Chương 2 > Thiết kế CSDL",
                Snippet = "Cơ sở dữ liệu của hệ thống được chuẩn hóa theo dạng 3NF.",
                CreatedAt = DateTime.UtcNow
            };

            db.Documents.Add(doc);
            db.DocumentVersions.Add(version);
            db.DocumentChunks.Add(chunk);
            db.ChatSessions.Add(session);
            db.ChatMessages.Add(message);
            db.ChatMessageCitations.Add(citation);
            await db.SaveChangesAsync();
        }

        var tokenOwner = _factory.GenerateJwtToken(800, "chatUserA", "STUDENT");
        var tokenStranger = _factory.GenerateJwtToken(801, "chatUserB", "STUDENT");

        // 1. Owner can fetch session messages and receive full citations
        var reqOwner = new HttpRequestMessage(HttpMethod.Get, $"/api/chat/sessions/{sessionId}/messages");
        reqOwner.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenOwner);
        var resOwner = await _client.SendAsync(reqOwner);

        Assert.Equal(HttpStatusCode.OK, resOwner.StatusCode);
        var messages = await resOwner.Content.ReadFromJsonAsync<List<ChatMessageDto>>();
        Assert.NotNull(messages);
        var botMsg = messages.FirstOrDefault(m => m.Sender == "BOT");
        Assert.NotNull(botMsg);
        Assert.Single(botMsg.Citations);

        var firstCitation = botMsg.Citations[0];
        Assert.Equal(chunkId, firstCitation.ChunkId);
        Assert.Equal(docId, firstCitation.DocumentId);
        Assert.Equal(versionId, firstCitation.DocumentVersionId);
        Assert.Equal("Kiến trúc hệ thống phần mềm", firstCitation.DocumentTitle);
        Assert.Equal(5, firstCitation.PageNumber);
        Assert.Equal("Cơ sở dữ liệu của hệ thống được chuẩn hóa theo dạng 3NF.", firstCitation.Snippet);

        // 2. Stranger gets empty list / blocked when querying another user's session
        var reqStranger = new HttpRequestMessage(HttpMethod.Get, $"/api/chat/sessions/{sessionId}/messages");
        reqStranger.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenStranger);
        var resStranger = await _client.SendAsync(reqStranger);

        Assert.Equal(HttpStatusCode.OK, resStranger.StatusCode);
        var strangerMessages = await resStranger.Content.ReadFromJsonAsync<List<ChatMessageDto>>();
        Assert.Empty(strangerMessages!);
    }

    [Fact]
    public async Task Delete_ChatMessage_Cascades_To_Citations()
    {
        await SeedBaseDataAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();

            var doc = new Document { UserId = 800, Title = "Doc Cascade", FileExtension = "txt", CloudStorageUrl = "/u/c.txt" };
            var version = new DocumentVersion { Document = doc, VersionNumber = 1, CloudStorageUrl = "/u/c_v1.txt", FileExtension = "txt", CreatedByUserId = 800 };
            var chunk = new DocumentChunk { Document = doc, DocumentVersion = version, ChunkIndex = 0, Text = "Cascade Chunk Text" };
            var session = new ChatSession { UserId = 800, SessionName = "Cascade Test Session" };
            var message = new ChatMessage { Session = session, Sender = "BOT", MessageContent = "Cascade Msg" };
            var citation = new ChatMessageCitation
            {
                Message = message,
                Document = doc,
                DocumentVersion = version,
                Chunk = chunk,
                DocumentTitleSnapshot = "Doc Cascade",
                VersionNumberSnapshot = 1,
                FileExtensionSnapshot = "txt",
                Snippet = "Cascade Chunk Text",
                CreatedAt = DateTime.UtcNow
            };

            db.Documents.Add(doc);
            db.DocumentVersions.Add(version);
            db.DocumentChunks.Add(chunk);
            db.ChatSessions.Add(session);
            db.ChatMessages.Add(message);
            db.ChatMessageCitations.Add(citation);
            await db.SaveChangesAsync();

            int messageId = message.MessageId;

            // Verify citation is present
            Assert.True(await db.ChatMessageCitations.AnyAsync(c => c.MessageId == messageId));

            // Delete the message
            db.ChatMessages.Remove(message);
            await db.SaveChangesAsync();

            // Verify citation is cascaded and deleted
            Assert.False(await db.ChatMessageCitations.AnyAsync(c => c.MessageId == messageId));
        }
    }

    [Fact]
    public async Task Delete_DocumentChunk_Sets_ChunkId_To_Null_While_Preserving_Citation_Snapshot()
    {
        await SeedBaseDataAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();

            var doc = new Document { UserId = 800, Title = "Doc Rechunk Test", FileExtension = "txt", CloudStorageUrl = "/u/r.txt" };
            var version = new DocumentVersion { Document = doc, VersionNumber = 1, CloudStorageUrl = "/u/r_v1.txt", FileExtension = "txt", CreatedByUserId = 800 };
            var chunk = new DocumentChunk { Document = doc, DocumentVersion = version, ChunkIndex = 0, PageNumber = 2, Text = "Rechunk snippet text that should survive" };
            var session = new ChatSession { UserId = 800, SessionName = "Rechunk Test Session" };
            var message = new ChatMessage { Session = session, Sender = "BOT", MessageContent = "Rechunk Msg" };
            var citation = new ChatMessageCitation
            {
                Message = message,
                Document = doc,
                DocumentVersion = version,
                Chunk = chunk,
                DocumentTitleSnapshot = "Doc Rechunk Test",
                VersionNumberSnapshot = 1,
                FileExtensionSnapshot = "txt",
                PageNumberSnapshot = 2,
                Snippet = "Rechunk snippet text that should survive",
                CreatedAt = DateTime.UtcNow
            };

            db.Documents.Add(doc);
            db.DocumentVersions.Add(version);
            db.DocumentChunks.Add(chunk);
            db.ChatSessions.Add(session);
            db.ChatMessages.Add(message);
            db.ChatMessageCitations.Add(citation);
            await db.SaveChangesAsync();

            long citationId = citation.CitationId;

            // Simulate re-chunking: deleting the old chunk
            db.DocumentChunks.Remove(chunk);
            await db.SaveChangesAsync();

            // Verify citation still exists, ChunkId is null, and snapshot metadata is intact
            var preservedCitation = await db.ChatMessageCitations.FirstAsync(c => c.CitationId == citationId);
            Assert.Null(preservedCitation.ChunkId);
            Assert.Equal("Doc Rechunk Test", preservedCitation.DocumentTitleSnapshot);
            Assert.Equal(2, preservedCitation.PageNumberSnapshot);
            Assert.Equal("Rechunk snippet text that should survive", preservedCitation.Snippet);
        }
    }

    [Fact]
    public async Task ProcessUserMessage_With_Grounded_Response_Persists_AssistantMessage_And_Citations_Atomically()
    {
        await SeedBaseDataAsync();

        int sessionId = 8004;
        int docId = 8104;
        int versionId = 8204;
        int chunkId = 8304;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();

            var doc = new Document
            {
                DocumentId = docId,
                UserId = 800,
                Title = "Tài liệu môn Công nghệ phần mềm",
                FileExtension = "pdf",
                CloudStorageUrl = "/u/cnpm.pdf",
                SharingPermission = "PRIVATE",
                AiParsingStatus = "READY"
            };
            var version = new DocumentVersion
            {
                VersionId = versionId,
                DocumentId = docId,
                VersionNumber = 1,
                CloudStorageUrl = "/u/cnpm_v1.pdf",
                FileExtension = "pdf",
                CreatedByUserId = 800
            };
            var chunk = new DocumentChunk
            {
                ChunkId = chunkId,
                DocumentId = docId,
                DocumentVersionId = versionId,
                ChunkIndex = 0,
                PageNumber = 12,
                StartOffset = 0,
                EndOffset = 180,
                HeadingPath = "Chương 4 > Kiểm thử tự động",
                Text = "Kiểm thử tự động giúp phát hiện sớm các lỗi hồi quy trong quá trình phát triển."
            };
            var session = new ChatSession
            {
                SessionId = sessionId,
                UserId = 800,
                SessionName = "Phiên hỏi bài CNPM",
                AttachedDocumentId = docId
            };

            db.Documents.Add(doc);
            db.DocumentVersions.Add(version);
            db.DocumentChunks.Add(chunk);
            db.ChatSessions.Add(session);
            await db.SaveChangesAsync();

            // Fake Gemini multi-step: Step 1 emits VIEW command, Step 2 returns grounded response with chunk marker
            var fakeGemini = new FakeGeminiService(
                $"VIEW/{docId}",
                $"RESPONSE: Theo tài liệu ở Chương 4, kiểm thử tự động giúp phát hiện sớm lỗi hồi quy. [CHUNK:{chunkId}]"
            );
            var permissionService = scope.ServiceProvider.GetRequiredService<AIStudyHub.Application.Interfaces.IPermissionService>();
            var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var chatService = new AIStudyHub.Application.Services.ChatService(db, fakeGemini, permissionService, config);

            var answer = await chatService.ProcessUserMessageAsync(800, sessionId, new AskQuestionDto
            {
                MessageContent = "Kiểm thử tự động có lợi ích gì?",
                DocumentId = docId
            });

            // 1. Assert ChatAnswerDto contains response and citations
            Assert.NotNull(answer);
            Assert.Contains("kiểm thử tự động", answer.Response);
            Assert.Single(answer.Citations);

            var citation = answer.Citations[0];
            Assert.Equal(chunkId, citation.ChunkId);
            Assert.Equal(docId, citation.DocumentId);
            Assert.Equal(versionId, citation.DocumentVersionId);
            Assert.Equal(12, citation.PageNumber);
            Assert.Equal("Tài liệu môn Công nghệ phần mềm", citation.DocumentTitle);
            Assert.Equal("Kiểm thử tự động giúp phát hiện sớm các lỗi hồi quy trong quá trình phát triển.", citation.Snippet);

            // 2. Assert database state: BOT message and citation row are saved atomically in same transaction
            var botMessage = await db.ChatMessages.Include(m => m.Citations)
                .FirstOrDefaultAsync(m => m.SessionId == sessionId && m.Sender == "BOT" && m.Display == true);

            Assert.NotNull(botMessage);
            Assert.Single(botMessage.Citations);
            var dbCitation = botMessage.Citations.First();
            Assert.Equal(chunkId, dbCitation.ChunkId);
            Assert.Equal(docId, dbCitation.DocumentId);
            Assert.Equal(versionId, dbCitation.DocumentVersionId);
            Assert.Equal(1, dbCitation.VersionNumberSnapshot);
            Assert.Equal(12, dbCitation.PageNumberSnapshot);
        }
    }

    [Fact]
    public async Task DocumentChunker_To_Database_To_Chat_Citation_Carries_DocumentVersionId()
    {
        await SeedBaseDataAsync();

        int sessionId = 8005;
        int docId = 8105;
        int versionId = 8205;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestStudyHubDbContext>();

            var doc = new Document
            {
                DocumentId = docId,
                UserId = 800,
                Title = "Tài liệu Kiểm thử Phần mềm Chuyên sâu",
                FileExtension = "docx",
                CloudStorageUrl = "/u/test_v2.docx",
                CurrentVersionId = versionId,
                SharingPermission = "PRIVATE",
                AiParsingStatus = "READY"
            };
            var version = new DocumentVersion
            {
                VersionId = versionId,
                DocumentId = docId,
                VersionNumber = 2,
                CloudStorageUrl = "/u/test_v2.docx",
                FileExtension = "docx",
                CreatedByUserId = 800
            };
            var session = new ChatSession
            {
                SessionId = sessionId,
                UserId = 800,
                SessionName = "Phiên hỏi bài Kiểm thử",
                AttachedDocumentId = docId
            };

            db.Documents.Add(doc);
            db.DocumentVersions.Add(version);
            db.ChatSessions.Add(session);
            await db.SaveChangesAsync();

            // Run the actual production DocumentChunker passing the versionId
            string extractedContent = "Chương 1. Kiểm thử tích hợp\n\nKiểm thử tích hợp đảm bảo các module kết nối đồng bộ.";
            var generatedChunks = AIStudyHub.Application.Services.DocumentChunker.Chunk(
                docId,
                extractedContent,
                DateTime.UtcNow,
                null,
                versionId
            );

            db.DocumentChunks.AddRange(generatedChunks);
            await db.SaveChangesAsync();

            int generatedChunkId = generatedChunks[0].ChunkId;

            // Verify generated chunk has DocumentVersionId populated
            Assert.Equal(versionId, generatedChunks[0].DocumentVersionId);

            // Execute chat pipeline
            var fakeGemini = new FakeGeminiService(
                $"VIEW/{docId}",
                $"RESPONSE: Kiểm thử tích hợp đảm bảo các module kết nối đồng bộ. [CHUNK:{generatedChunkId}]"
            );
            var permissionService = scope.ServiceProvider.GetRequiredService<AIStudyHub.Application.Interfaces.IPermissionService>();
            var config = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            var chatService = new AIStudyHub.Application.Services.ChatService(db, fakeGemini, permissionService, config);

            var answer = await chatService.ProcessUserMessageAsync(800, sessionId, new AskQuestionDto
            {
                MessageContent = "Kiểm thử tích hợp là gì?",
                DocumentId = docId
            });

            // Assert citation carries the exact versionId = 8205 and version number = 2
            Assert.NotNull(answer);
            Assert.Single(answer.Citations);
            var citation = answer.Citations[0];
            Assert.Equal(generatedChunkId, citation.ChunkId);
            Assert.Equal(docId, citation.DocumentId);
            Assert.Equal(versionId, citation.DocumentVersionId);
            Assert.Equal(2, citation.VersionNumber);
            Assert.Equal("Tài liệu Kiểm thử Phần mềm Chuyên sâu", citation.DocumentTitle);
        }
    }
}

public class FakeGeminiService : AIStudyHub.Application.Interfaces.IGeminiService
{
    private readonly Queue<string> _responses = new();

    public FakeGeminiService(params string[] responses)
    {
        foreach (var r in responses)
        {
            _responses.Enqueue(r);
        }
    }

    public Task<GeminiResponseDto> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory, string operation = "CHAT", System.Threading.CancellationToken cancellationToken = default)
    {
        string content = _responses.Count > 0 ? _responses.Dequeue() : "RESPONSE: Mặc định.";
        return Task.FromResult(new GeminiResponseDto
        {
            Content = content,
            PromptTokens = 50,
            CompletionTokens = 30,
            TotalTokens = 80,
            Status = "SUCCESS"
        });
    }

    public Task<string> ExtractTextFromImageAsync(byte[] imageBytes, string mimeType, System.Threading.CancellationToken cancellationToken = default)
    {
        return Task.FromResult("Fake OCR Text");
    }
}
