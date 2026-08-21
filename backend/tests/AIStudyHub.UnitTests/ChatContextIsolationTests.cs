using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AIStudyHub.UnitTests;

public class ChatContextIsolationTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly TestStudyHubDbContext _db;
    private readonly FakeGeminiService _geminiFake;
    private readonly FakePermissionService _permissionFake;
    private readonly IConfiguration _config;
    private readonly ChatService _chatService;

    public ChatContextIsolationTests()
    {
        _factory = new TestDbContextFactory();
        _db = _factory.CreateContext();

        _geminiFake = new FakeGeminiService();
        _permissionFake = new FakePermissionService();

        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Gemini:MaxInputTokensPerRequest", "30000"},
            {"Gemini:MaxHistoryTokens", "20000"},
            {"Gemini:MaxContextTokens", "8000"}
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        _chatService = new ChatService(_db, _geminiFake, _permissionFake, _config);
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
    }

    private async Task SeedDefaultTierAsync()
    {
        if (!await _db.Subscriptions.AnyAsync(s => s.TierId == 1))
        {
            _db.Subscriptions.Add(new Subscription
            {
                TierId = 1,
                TierName = "FREE",
                AiPromptLimitPerDay = 100,
                Price = 0,
                MaxStorageMb = 100
            });
            await _db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task CreateSession_WithDocument_UsesDocumentTitle_AndPinsAttachmentImmediately()
    {
        await SeedDefaultTierAsync();

        var user = new User { UserId = 6, Username = "document-chat-user", Email = "document-chat@test.com", Role = "STUDENT", TierId = 1 };
        var document = new Document
        {
            DocumentId = 60,
            UserId = user.UserId,
            Title = "Giáo trình Cơ sở dữ liệu",
            FileExtension = "pdf",
            CloudStorageUrl = "http://storage/database.pdf",
            AiParsingStatus = "READY",
            GeneralAccess = "PUBLIC"
        };
        _db.Users.Add(user);
        _db.Documents.Add(document);
        await _db.SaveChangesAsync();

        var version = new DocumentVersion
        {
            VersionId = 601,
            DocumentId = document.DocumentId,
            VersionNumber = 1,
            CloudStorageUrl = "http://storage/database-v1.pdf",
            FileExtension = "pdf",
            CreatedByUserId = user.UserId
        };
        _db.DocumentVersions.Add(version);
        document.CurrentVersionId = version.VersionId;
        await _db.SaveChangesAsync();

        var result = await _chatService.CreateSessionAsync(user.UserId, new CreateSessionDto
        {
            SessionName = "Tên không được sử dụng",
            DocumentId = document.DocumentId
        });

        Assert.Equal(document.Title, result.SessionName);
        Assert.Equal(document.DocumentId, result.AttachedDocumentId);
        Assert.Equal(document.Title, result.AttachedDocumentTitle);
        Assert.Equal(version.VersionId, result.AttachedDocumentVersionId);
        Assert.Equal(1, result.CurrentAttachmentEpoch);

        var persistedSession = await _db.ChatSessions.AsNoTracking().SingleAsync(s => s.SessionId == result.SessionId);
        Assert.Equal(document.DocumentId, persistedSession.AttachedDocumentId);
        Assert.Equal(version.VersionId, persistedSession.AttachedDocumentVersionId);
        Assert.True(await _db.ChatMessages.AnyAsync(m =>
            m.SessionId == result.SessionId &&
            m.MessageKind == "DOCUMENT_CONTEXT" &&
            m.ContextDocumentId == document.DocumentId &&
            m.ContextDocumentVersionId == version.VersionId));
    }

    [Fact]
    public async Task ChatSession_AttachmentEpoch_Increments_When_Switching_Attached_Documents()
    {
        await SeedDefaultTierAsync();

        var user = new User { UserId = 1, Username = "testuser", Email = "test@example.com", Role = "STUDENT", TierId = 1 };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var docA = new Document { DocumentId = 10, Title = "Doc A", FileExtension = "pdf", CloudStorageUrl = "http://storage/a", AiParsingStatus = "READY", GeneralAccess = "PUBLIC", UserId = 1, CurrentVersionId = 101 };
        var docB = new Document { DocumentId = 20, Title = "Doc B", FileExtension = "pdf", CloudStorageUrl = "http://storage/b", AiParsingStatus = "READY", GeneralAccess = "PUBLIC", UserId = 1, CurrentVersionId = 201 };
        _db.Documents.AddRange(docA, docB);
        await _db.SaveChangesAsync();

        var verA = new DocumentVersion { VersionId = 101, DocumentId = 10, VersionNumber = 1, CloudStorageUrl = "http://a", FileExtension = "pdf", CreatedByUserId = 1 };
        var verB = new DocumentVersion { VersionId = 201, DocumentId = 20, VersionNumber = 1, CloudStorageUrl = "http://b", FileExtension = "pdf", CreatedByUserId = 1 };
        _db.DocumentVersions.AddRange(verA, verB);
        await _db.SaveChangesAsync();

        var sessionDto = await _chatService.CreateSessionAsync(1, new CreateSessionDto { SessionName = "Isolation Session" });
        Assert.Equal(0, sessionDto.CurrentAttachmentEpoch);

        // Attach Doc A
        var attachedA = await _chatService.SetAttachedDocumentAsync(1, sessionDto.SessionId, 10);
        Assert.NotNull(attachedA);
        Assert.Equal(1, attachedA.CurrentAttachmentEpoch);
        Assert.Equal(10, attachedA.AttachedDocumentId);
        Assert.Equal(101, attachedA.AttachedDocumentVersionId);

        // Switch to Doc B
        var attachedB = await _chatService.SetAttachedDocumentAsync(1, sessionDto.SessionId, 20);
        Assert.NotNull(attachedB);
        Assert.Equal(2, attachedB.CurrentAttachmentEpoch);
        Assert.Equal(20, attachedB.AttachedDocumentId);
        Assert.Equal(201, attachedB.AttachedDocumentVersionId);
    }

    [Fact]
    public async Task AskQuestion_Cannot_Override_The_Document_Locked_To_The_Session()
    {
        await SeedDefaultTierAsync();
        var user = new User { UserId = 7, Username = "locked-user", Email = "locked@test.com", Role = "STUDENT", TierId = 1 };
        var lockedDoc = new Document { DocumentId = 70, UserId = 7, Title = "Locked Doc", FileExtension = "txt", CloudStorageUrl = "locked.txt", AiParsingStatus = "READY", GeneralAccess = "PUBLIC", CurrentVersionId = 701 };
        var otherDoc = new Document { DocumentId = 71, UserId = 7, Title = "Other Doc", FileExtension = "txt", CloudStorageUrl = "other.txt", AiParsingStatus = "READY", GeneralAccess = "PUBLIC", CurrentVersionId = 711 };
        _db.Users.Add(user);
        _db.Documents.AddRange(lockedDoc, otherDoc);
        await _db.SaveChangesAsync();
        _db.DocumentVersions.AddRange(
            new DocumentVersion { VersionId = 701, DocumentId = 70, VersionNumber = 1, CloudStorageUrl = "locked-v1.txt", FileExtension = "txt", CreatedByUserId = 7 },
            new DocumentVersion { VersionId = 711, DocumentId = 71, VersionNumber = 1, CloudStorageUrl = "other-v1.txt", FileExtension = "txt", CreatedByUserId = 7 });
        await _db.SaveChangesAsync();

        var session = await _chatService.CreateSessionAsync(7, new CreateSessionDto { SessionName = "ignored", DocumentId = 70 });

        await Assert.ThrowsAsync<ArgumentException>(() => _chatService.ProcessUserMessageAsync(7, session.SessionId,
            new AskQuestionDto { MessageContent = "Đọc tài liệu khác", DocumentId = 71 }));

        var persisted = await _db.ChatSessions.AsNoTracking().SingleAsync(s => s.SessionId == session.SessionId);
        Assert.Equal(70, persisted.AttachedDocumentId);
        Assert.False(await _db.ChatMessages.AnyAsync(m => m.SessionId == session.SessionId && m.Display == true));
    }

    [Fact]
    public async Task Retry_Reuses_The_Failed_UserMessage_Without_Creating_A_Duplicate()
    {
        await SeedDefaultTierAsync();
        var user = new User { UserId = 8, Username = "retry-user", Email = "retry@test.com", Role = "STUDENT", TierId = 1 };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        var session = await _chatService.CreateSessionAsync(8, new CreateSessionDto { SessionName = "Retry" });

        _geminiFake.ResponseFactory = _ => throw new TimeoutException("Temporary AI failure");
        await Assert.ThrowsAsync<TimeoutException>(() => _chatService.ProcessUserMessageAsync(8, session.SessionId,
            new AskQuestionDto { MessageContent = "Câu hỏi cần thử lại" }));

        var failedMessage = await _db.ChatMessages.SingleAsync(m =>
            m.SessionId == session.SessionId && m.Display == true && m.Sender == "USER");
        _geminiFake.ResponseFactory = _ => new GeminiResponseDto
        {
            Content = "{\"answer\":\"Thử lại thành công.\",\"citations\":[],\"insufficientContext\":false}"
        };

        var result = await _chatService.ProcessUserMessageAsync(8, session.SessionId, new AskQuestionDto
        {
            MessageContent = "Nội dung phía client không được dùng",
            RetryMessageId = failedMessage.MessageId
        });

        Assert.Equal("Thử lại thành công.", result.Response);
        Assert.Equal(1, await _db.ChatMessages.CountAsync(m =>
            m.SessionId == session.SessionId && m.Display == true && m.Sender == "USER"));
    }

    [Fact]
    public async Task LegacyDocument_WithoutVersion_GetsBaselineBeforeCitationPersistence()
    {
        await SeedDefaultTierAsync();
        var user = new User { UserId = 9, Username = "legacy-user", Email = "legacy@test.com", Role = "STUDENT", TierId = 1 };
        var document = new Document
        {
            DocumentId = 90,
            UserId = 9,
            Title = "Legacy Document",
            FileExtension = "txt",
            CloudStorageUrl = "legacy.txt",
            FileSizeMb = 1,
            AiParsingStatus = "READY",
            GeneralAccess = "PUBLIC"
        };
        var chunk = new DocumentChunk { ChunkId = 9001, DocumentId = 90, DocumentVersionId = null, ChunkIndex = 0, Text = "Nội dung legacy có căn cứ." };
        _db.Users.Add(user);
        _db.Documents.Add(document);
        _db.DocumentChunks.Add(chunk);
        await _db.SaveChangesAsync();

        var session = await _chatService.CreateSessionAsync(9, new CreateSessionDto { SessionName = "ignored", DocumentId = 90 });
        Assert.NotNull(session.AttachedDocumentVersionId);
        Assert.True(await _db.DocumentVersions.AnyAsync(v => v.VersionId == session.AttachedDocumentVersionId && v.DocumentId == 90));
        Assert.Equal(session.AttachedDocumentVersionId, (await _db.DocumentChunks.SingleAsync(c => c.ChunkId == 9001)).DocumentVersionId);

        var calls = 0;
        _geminiFake.ResponseFactory = _ => ++calls == 1
            ? new GeminiResponseDto { Content = "VIEW/90" }
            : new GeminiResponseDto { Content = "{\"answer\":\"Câu trả lời legacy.\",\"citations\":[{\"chunkId\":9001}],\"insufficientContext\":false}" };

        var answer = await _chatService.ProcessUserMessageAsync(9, session.SessionId,
            new AskQuestionDto { MessageContent = "Tài liệu nói gì?", DocumentId = 90 });

        Assert.Single(answer.Citations);
        Assert.Equal(session.AttachedDocumentVersionId, answer.Citations[0].DocumentVersionId);
    }

    [Fact]
    public async Task AttachedDocument_ResponsePrefix_WithJson_IsReturnedAsNaturalAnswer()
    {
        await SeedDefaultTierAsync();
        var user = new User { UserId = 10, Username = "json-user", Email = "json@test.com", Role = "STUDENT", TierId = 1 };
        var document = new Document
        {
            DocumentId = 100, UserId = 10, Title = "Task", FileExtension = "txt", CloudStorageUrl = "task.txt",
            AiParsingStatus = "READY", GeneralAccess = "PUBLIC", CurrentVersionId = 1001
        };
        _db.Users.Add(user);
        _db.Documents.Add(document);
        await _db.SaveChangesAsync();
        _db.DocumentVersions.Add(new DocumentVersion
        {
            VersionId = 1001, DocumentId = 100, VersionNumber = 1, CloudStorageUrl = "task-v1.txt",
            FileExtension = "txt", CreatedByUserId = 10
        });
        _db.DocumentChunks.Add(new DocumentChunk
        {
            ChunkId = 10001, DocumentId = 100, DocumentVersionId = 1001, ChunkIndex = 0,
            Text = "Sau khi check-in, bàn bị khóa trong tất cả các khung giờ.", PageNumber = 1
        });
        await _db.SaveChangesAsync();

        var session = await _chatService.CreateSessionAsync(10, new CreateSessionDto { DocumentId = 100 });
        var calls = 0;
        _geminiFake.ResponseFactory = _ => ++calls == 1
            ? new GeminiResponseDto { Content = "VIEW/100" }
            : new GeminiResponseDto
            {
                Content = "RESPONSE: {\"answer\":\"Bàn bị khóa trong tất cả các khung giờ [CHUNK:10001].\",\"citations\":[{\"chunkId\":10001,\"page\":1}],\"insufficientContext\":false}"
            };

        var result = await _chatService.ProcessUserMessageAsync(10, session.SessionId,
            new AskQuestionDto { MessageContent = "Quy tắc khóa bàn là gì?", DocumentId = 100 });

        Assert.DoesNotContain("{\"answer\"", result.Response);
        Assert.StartsWith("Bàn bị khóa trong tất cả các khung giờ", result.Response);
        Assert.Single(result.Citations);
        Assert.Equal(10001, result.Citations[0].ChunkId);
    }

    [Fact]
    public async Task UnattachedInventoryQuestion_CanAnswerAfterSearch_WithoutViewingAChunk()
    {
        await SeedDefaultTierAsync();
        var user = new User { UserId = 11, Username = "inventory-user", Email = "inventory@test.com", Role = "STUDENT", TierId = 1 };
        var otherUser = new User { UserId = 999, Username = "other-user", Email = "other@test.com", Role = "STUDENT", TierId = 1 };
        _db.Users.AddRange(user, otherUser);
        _db.Documents.AddRange(
            new Document { DocumentId = 110, UserId = 11, Title = "Tài liệu A", FileExtension = "txt", CloudStorageUrl = "a.txt", AiParsingStatus = "READY", GeneralAccess = "PUBLIC" },
            new Document { DocumentId = 111, UserId = 11, Title = "Tài liệu B", FileExtension = "txt", CloudStorageUrl = "b.txt", AiParsingStatus = "READY", GeneralAccess = "PUBLIC" },
            new Document { DocumentId = 112, UserId = 999, Title = "Tài liệu public của người khác", FileExtension = "txt", CloudStorageUrl = "other.txt", AiParsingStatus = "READY", GeneralAccess = "PUBLIC" });
        await _db.SaveChangesAsync();

        var session = await _chatService.CreateSessionAsync(11, new CreateSessionDto { SessionName = "Inventory" });
        _permissionFake.ViewableDocumentIds = [110, 111];
        _geminiFake.ResponseFactory = _ => throw new InvalidOperationException("Inventory must not call Gemini.");

        var result = await _chatService.ProcessUserMessageAsync(11, session.SessionId,
            new AskQuestionDto { MessageContent = "Hiện tại bạn xem được bao nhiêu tài liệu?" });

        Assert.Contains("2 tài liệu", result.Response);
        Assert.Contains("- Tài liệu A", result.Response);
        Assert.Contains("- Tài liệu B", result.Response);
        Assert.DoesNotContain("Tài liệu public của người khác", result.Response);
        Assert.DoesNotContain("Không tìm thấy tài liệu", result.Response);
        Assert.DoesNotContain("hệ thống AI đang gặp sự cố", result.Response);
    }

    [Fact]
    public async Task AttachedInventoryQuestion_ReportsOnlyLockedDocument_WithoutCallingGemini()
    {
        await SeedDefaultTierAsync();
        var user = new User { UserId = 12, Username = "locked-inventory", Email = "locked-inventory@test.com", Role = "STUDENT", TierId = 1 };
        var document = new Document
        {
            DocumentId = 120, UserId = 12, Title = "Tài liệu được khóa", FileExtension = "txt",
            CloudStorageUrl = "locked-inventory.txt", AiParsingStatus = "READY", GeneralAccess = "PUBLIC",
            CurrentVersionId = 1201
        };
        _db.Users.Add(user);
        _db.Documents.Add(document);
        await _db.SaveChangesAsync();
        _db.DocumentVersions.Add(new DocumentVersion
        {
            VersionId = 1201, DocumentId = 120, VersionNumber = 1, CloudStorageUrl = "locked-inventory-v1.txt",
            FileExtension = "txt", CreatedByUserId = 12
        });
        await _db.SaveChangesAsync();

        var session = await _chatService.CreateSessionAsync(12, new CreateSessionDto { DocumentId = 120 });
        _geminiFake.ResponseFactory = _ => throw new InvalidOperationException("Attached inventory must not call Gemini.");

        var result = await _chatService.ProcessUserMessageAsync(12, session.SessionId,
            new AskQuestionDto { MessageContent = "AI có thể xem được tài liệu nào?", DocumentId = 120 });

        Assert.Contains("duy nhất tài liệu “Tài liệu được khóa”", result.Response);
        Assert.Contains("không đọc tài liệu nào khác", result.Response);
    }

    [Fact]
    public async Task Prompt_History_Excludes_Messages_From_Previous_Attachment_Epochs()
    {
        await SeedDefaultTierAsync();

        var user = new User { UserId = 2, Username = "user2", Email = "user2@test.com", Role = "STUDENT", TierId = 1 };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var docA = new Document { DocumentId = 11, Title = "Document Alpha", FileExtension = "txt", CloudStorageUrl = "http://storage/11", AiParsingStatus = "READY", GeneralAccess = "PUBLIC", UserId = 2, CurrentVersionId = 111 };
        var docB = new Document { DocumentId = 22, Title = "Document Beta", FileExtension = "txt", CloudStorageUrl = "http://storage/22", AiParsingStatus = "READY", GeneralAccess = "PUBLIC", UserId = 2, CurrentVersionId = 222 };
        _db.Documents.AddRange(docA, docB);
        await _db.SaveChangesAsync();

        var verA = new DocumentVersion { VersionId = 111, DocumentId = 11, VersionNumber = 1, CloudStorageUrl = "http://a", FileExtension = "txt", CreatedByUserId = 2 };
        var verB = new DocumentVersion { VersionId = 222, DocumentId = 22, VersionNumber = 1, CloudStorageUrl = "http://b", FileExtension = "txt", CreatedByUserId = 2 };
        _db.DocumentVersions.AddRange(verA, verB);
        await _db.SaveChangesAsync();

        var chunkA = new DocumentChunk { ChunkId = 1, DocumentId = 11, DocumentVersionId = 111, ChunkIndex = 0, Text = "Secret recipe is Apple Pie." };
        var chunkB = new DocumentChunk { ChunkId = 2, DocumentId = 22, DocumentVersionId = 222, ChunkIndex = 0, Text = "Beta document info." };
        _db.DocumentChunks.AddRange(chunkA, chunkB);
        await _db.SaveChangesAsync();

        var session = await _chatService.CreateSessionAsync(2, new CreateSessionDto { SessionName = "Epoch Filter Session" });

        // User attaches Doc A and asks question in Epoch 1
        await _chatService.SetAttachedDocumentAsync(2, session.SessionId, 11);

        _geminiFake.ResponseFactory = (history) => new GeminiResponseDto { Content = "RESPONSE: Công thức bí mật là Apple Pie." };

        await _chatService.ProcessUserMessageAsync(2, session.SessionId, new AskQuestionDto { MessageContent = "Bí mật trong Doc A là gì?" });

        // Now user switches attachment to Doc B (Epoch 2)
        await _chatService.SetAttachedDocumentAsync(2, session.SessionId, 22);

        List<ChatMessageDto>? promptPassedToGeminiInEpoch2 = null;
        _geminiFake.ResponseFactory = (history) =>
        {
            promptPassedToGeminiInEpoch2 = history.ToList();
            return new GeminiResponseDto { Content = "RESPONSE: Nội dung Beta là thông tin chung." };
        };

        await _chatService.ProcessUserMessageAsync(2, session.SessionId, new AskQuestionDto { MessageContent = "Doc B nói gì?" });

        Assert.NotNull(promptPassedToGeminiInEpoch2);
        var promptTexts = string.Join(" ", promptPassedToGeminiInEpoch2.Select(m => m.MessageContent));
        Assert.DoesNotContain("Apple Pie", promptTexts);
        Assert.DoesNotContain("Bí mật trong Doc A là gì?", promptTexts);
        Assert.Contains("Doc B nói gì?", promptTexts);
    }

    [Fact]
    public async Task Version_Pinned_Retrieval_Only_Fetches_Chunks_Matching_Pinned_Version()
    {
        await SeedDefaultTierAsync();

        var user = new User { UserId = 3, Username = "user3", Email = "user3@test.com", Role = "STUDENT", TierId = 1 };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var doc = new Document { DocumentId = 30, Title = "Conflicting Doc", FileExtension = "txt", CloudStorageUrl = "http://storage/30", AiParsingStatus = "READY", GeneralAccess = "PUBLIC", UserId = 3, CurrentVersionId = 302 };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        var ver1 = new DocumentVersion { VersionId = 301, DocumentId = 30, VersionNumber = 1, CloudStorageUrl = "http://v1", FileExtension = "txt", CreatedByUserId = 3 };
        var ver2 = new DocumentVersion { VersionId = 302, DocumentId = 30, VersionNumber = 2, CloudStorageUrl = "http://v2", FileExtension = "txt", CreatedByUserId = 3 };
        _db.DocumentVersions.AddRange(ver1, ver2);
        await _db.SaveChangesAsync();

        var chunkV1 = new DocumentChunk { ChunkId = 3001, DocumentId = 30, DocumentVersionId = 301, ChunkIndex = 0, Text = "Thủ đô của nước Pháp là Paris (Version 1)." };
        var chunkV2 = new DocumentChunk { ChunkId = 3002, DocumentId = 30, DocumentVersionId = 302, ChunkIndex = 0, Text = "Thủ đô của nước Pháp là Lyon (Version 2)." };
        _db.DocumentChunks.AddRange(chunkV1, chunkV2);
        await _db.SaveChangesAsync();

        var session = await _chatService.CreateSessionAsync(3, new CreateSessionDto { SessionName = "Version Pinned Session" });

        // Pin explicitly to Version 1
        await _chatService.SetAttachedDocumentAsync(3, session.SessionId, 30, 301);

        int callCount = 0;
        string? contextReceivedByGemini = null;

        _geminiFake.ResponseFactory = (history) =>
        {
            callCount++;
            if (callCount == 1)
            {
                return new GeminiResponseDto
                {
                    Content = "{\"answer\":\"Tôi thấy nhiều tài liệu khác trong hệ thống.\",\"citations\":[],\"insufficientContext\":false}"
                };
            }
            if (callCount == 2)
            {
                return new GeminiResponseDto { Content = "VIEW/30" };
            }
            contextReceivedByGemini = history.Last().MessageContent;
            return new GeminiResponseDto
            {
                Content = "{\"answer\":\"Thủ đô là Paris theo v1.\",\"citations\":[{\"chunkId\":3001,\"page\":1}],\"insufficientContext\":false}"
            };
        };

        var answer = await _chatService.ProcessUserMessageAsync(3, session.SessionId, new AskQuestionDto { MessageContent = "Thủ đô là gì?" });

        Assert.NotNull(contextReceivedByGemini);
        Assert.Equal(3, callCount);
        Assert.Contains("Paris (Version 1)", contextReceivedByGemini);
        Assert.DoesNotContain("Lyon (Version 2)", contextReceivedByGemini);

        Assert.StartsWith("Thủ đô là Paris theo v1.", answer.Response);
        Assert.DoesNotContain("{\"answer\"", answer.Response);
        Assert.Single(answer.Citations);
        Assert.Equal(3001, answer.Citations[0].ChunkId);
        Assert.Equal(301, answer.Citations[0].DocumentVersionId);
    }

    [Fact]
    public async Task ResolveCitation_Returns_Immutable_Snapshot_And_Enforces_User_Permissions()
    {
        await SeedDefaultTierAsync();

        var owner = new User { UserId = 4, Username = "owner", Email = "owner@test.com", Role = "STUDENT", TierId = 1 };
        var outsider = new User { UserId = 5, Username = "outsider", Email = "outsider@test.com", Role = "STUDENT", TierId = 1 };
        _db.Users.AddRange(owner, outsider);
        await _db.SaveChangesAsync();

        var doc = new Document { DocumentId = 40, Title = "Original Title Snapshot", FileExtension = "pdf", CloudStorageUrl = "http://storage/40", AiParsingStatus = "READY", GeneralAccess = "RESTRICTED", UserId = 4, CurrentVersionId = 401 };
        _db.Documents.Add(doc);
        await _db.SaveChangesAsync();

        var version = new DocumentVersion { VersionId = 401, DocumentId = 40, VersionNumber = 1, CloudStorageUrl = "http://doc40", FileExtension = "pdf", CreatedByUserId = 4 };
        _db.DocumentVersions.Add(version);
        await _db.SaveChangesAsync();

        var chunk = new DocumentChunk { ChunkId = 999, DocumentId = 40, DocumentVersionId = 401, ChunkIndex = 0, Text = "Chunk 999 text" };
        _db.DocumentChunks.Add(chunk);
        await _db.SaveChangesAsync();

        var session = new ChatSession { SessionId = 400, UserId = 4, SessionName = "Owner Session" };
        _db.ChatSessions.Add(session);
        await _db.SaveChangesAsync();

        var msg = new ChatMessage { MessageId = 4000, SessionId = 400, Sender = "BOT", MessageContent = "Bot answer", Display = true };
        _db.ChatMessages.Add(msg);
        await _db.SaveChangesAsync();

        var citation = new ChatMessageCitation
        {
            CitationId = 40001,
            MessageId = 4000,
            DocumentId = 40,
            DocumentVersionId = 401,
            ChunkId = 999,
            DocumentTitleSnapshot = "Original Title Snapshot",
            VersionNumberSnapshot = 1,
            FileExtensionSnapshot = "pdf",
            PageNumberSnapshot = 3,
            StartOffsetSnapshot = 100,
            EndOffsetSnapshot = 250,
            Snippet = "Immutable snippet content that will never change.",
            CreatedAt = DateTime.UtcNow
        };
        _db.ChatMessageCitations.Add(citation);
        await _db.SaveChangesAsync();

        // 1. Owner resolves citation
        var ownerResult = await _chatService.ResolveCitationAsync(4, 40001);
        Assert.NotNull(ownerResult);
        Assert.Equal(40001, ownerResult.CitationId);
        Assert.Equal(401, ownerResult.DocumentVersionId);
        Assert.Equal("Immutable snippet content that will never change.", ownerResult.Snippet);
        Assert.Equal(3, ownerResult.PageNumber);

        // 2. Outsider without permissions fails
        _permissionFake.CanView = false;
        var outsiderResult = await _chatService.ResolveCitationAsync(5, 40001);
        Assert.Null(outsiderResult);
    }

    private class FakeGeminiService : IGeminiService
    {
        public Func<List<ChatMessageDto>, GeminiResponseDto> ResponseFactory { get; set; } =
            _ => new GeminiResponseDto { Content = "RESPONSE: Default mock response" };

        public Task<GeminiResponseDto> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory, string operation = "CHAT", CancellationToken cancellationToken = default)
            => Task.FromResult(ResponseFactory(messageHistory));

        public Task<string> ExtractTextFromImageAsync(byte[] imageBytes, string mimeType, CancellationToken cancellationToken = default)
            => Task.FromResult("Image text");
    }

    private class FakePermissionService : IPermissionService
    {
        public bool CanView { get; set; } = true;
        public List<int> ViewableDocumentIds { get; set; } = [];
        public List<int> SharedDocumentIds { get; set; } = [];

        public Task<string> GetEffectiveDocumentRoleAsync(int documentId, int userId, string? shareToken = null)
            => Task.FromResult("VIEWER");

        public Task<string> GetEffectiveFolderRoleAsync(int folderId, int userId)
            => Task.FromResult("VIEWER");

        public Task<bool> CanViewDocumentAsync(int documentId, int userId, string? shareToken = null)
            => Task.FromResult(CanView);

        public Task<bool> CanDownloadDocumentAsync(int documentId, int userId, string? shareToken = null)
            => Task.FromResult(true);

        public Task<bool> CanEditDocumentAsync(int documentId, int userId)
            => Task.FromResult(true);

        public Task<bool> CanManageDocumentAccessAsync(int documentId, int userId)
            => Task.FromResult(true);

        public Task<List<int>> GetSharedDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null)
            => Task.FromResult(SharedDocumentIds);

        public Task<List<int>> GetViewableDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null)
            => Task.FromResult(ViewableDocumentIds);

        public Task<List<int>> GetAccessibleDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null)
            => Task.FromResult(new List<int>());

        public Task<ItemAccessSettingsDto> GetDocumentAccessSettingsAsync(int documentId, int currentUserId)
            => Task.FromResult(new ItemAccessSettingsDto());

        public Task<ItemAccessSettingsDto> GetFolderAccessSettingsAsync(int folderId, int currentUserId)
            => Task.FromResult(new ItemAccessSettingsDto());

        public Task UpdateDocumentGeneralAccessAsync(int documentId, string generalAccess, int currentUserId)
            => Task.CompletedTask;

        public Task UpdateFolderGeneralAccessAsync(int folderId, string generalAccess, int currentUserId)
            => Task.CompletedTask;

        public Task AddOrUpdateDocumentUserShareAsync(int documentId, string email, string role, int currentUserId)
            => Task.CompletedTask;

        public Task AddOrUpdateFolderUserShareAsync(int folderId, string email, string role, int currentUserId)
            => Task.CompletedTask;

        public Task RemoveDocumentUserShareAsync(int documentId, int targetUserId, int currentUserId)
            => Task.CompletedTask;

        public Task RemoveFolderUserShareAsync(int folderId, int targetUserId, int currentUserId)
            => Task.CompletedTask;

        public Task<ShareLinkInfoDto> RotateDocumentShareLinkAsync(int documentId, int currentUserId)
            => Task.FromResult(new ShareLinkInfoDto());

        public Task RevokeDocumentShareLinkAsync(int documentId, int currentUserId)
            => Task.CompletedTask;

        public Task LogAuditAsync(int actorUserId, string action, string targetType, int targetId, string? details)
            => Task.CompletedTask;

        public Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(int pageNumber, int pageSize)
            => Task.FromResult(new PagedResult<AuditLogDto>());
    }
}
