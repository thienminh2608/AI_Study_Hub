using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AIStudyHub.UnitTests;

public class CitationExtractionTests
{
    [Fact]
    public void ExtractChunkIdsFromText_Extracts_Valid_Markers_And_Filters_Against_Whitelist_Using_Production_Method()
    {
        // Candidate whitelist sent in prompt
        var allowedIds = new HashSet<int> { 101, 102, 105 };

        string aiResponse = "Theo tài liệu, kiến trúc hệ thống gồm 3 tầng [CHUNK:101] và sử dụng EF Core [CHUNK id=102]. " +
                            "Tuy nhiên thông tin này không có thật [CHUNK:999] và [CHUNK:101].";

        // Call the REAL production ChatService.ExtractChunkIdsFromText method
        var extracted = ChatService.ExtractChunkIdsFromText(aiResponse, allowedIds);

        // 101 and 102 should be extracted, 999 is outside allowedIds and rejected, 101 is deduplicated
        Assert.Equal(2, extracted.Count);
        Assert.Contains(101, extracted);
        Assert.Contains(102, extracted);
        Assert.DoesNotContain(999, extracted);
    }

    [Fact]
    public void ExtractChunkIdsFromText_Handles_Empty_Or_Null_Gracefully()
    {
        var allowedIds = new HashSet<int> { 1, 2, 3 };

        Assert.Empty(ChatService.ExtractChunkIdsFromText(null!, allowedIds));
        Assert.Empty(ChatService.ExtractChunkIdsFromText("", allowedIds));
        Assert.Empty(ChatService.ExtractChunkIdsFromText("Không có trích dẫn nào ở đây", allowedIds));
        Assert.Empty(ChatService.ExtractChunkIdsFromText("[CHUNK:1]", new HashSet<int>()));
    }

    [Fact]
    public async Task PersistCitations_Truncates_Snippet_Safely_And_Pins_Version_Directly_From_Chunk()
    {
        using var factory = new TestDbContextFactory();
        using var db = factory.CreateContext();

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

        var user = new User { UserId = 1, Username = "student1", PasswordHash = "hash", Email = "s1@test.com", TierId = 1 };
        var doc = new Document
        {
            DocumentId = 10,
            UserId = 1,
            Title = "Original Architecture Document",
            FileExtension = "pdf",
            CloudStorageUrl = "/u/doc.pdf"
        };
        var version1 = new DocumentVersion
        {
            VersionId = 100,
            DocumentId = 10,
            VersionNumber = 1,
            CloudStorageUrl = "/u/v1.pdf",
            FileExtension = "pdf",
            CreatedByUserId = 1
        };
        var version2 = new DocumentVersion
        {
            VersionId = 101,
            DocumentId = 10,
            VersionNumber = 2,
            CloudStorageUrl = "/u/v2.pdf",
            FileExtension = "pdf",
            CreatedByUserId = 1
        };
        doc.CurrentVersionId = 101; // Latest version is 2, but chunk belongs to version 1

        string longText = new string('A', 3500); // Exceeds 2000 chars
        var chunk = new DocumentChunk
        {
            ChunkId = 500,
            DocumentId = 10,
            DocumentVersionId = 100, // Explicitly belongs to version 1!
            ChunkIndex = 0,
            PageNumber = 3,
            StartOffset = 100,
            EndOffset = 3600,
            HeadingPath = "Chapter 1 > Architecture",
            Text = longText
        };

        var session = new ChatSession { SessionId = 1, UserId = 1, SessionName = "Test Chat Session" };
        var message = new ChatMessage { MessageId = 1, SessionId = 1, Sender = "BOT", MessageContent = "AI Answer" };

        db.Users.Add(user);
        db.Documents.Add(doc);
        db.DocumentVersions.AddRange(version1, version2);
        db.DocumentChunks.Add(chunk);
        db.ChatSessions.Add(session);
        db.ChatMessages.Add(message);
        await db.SaveChangesAsync();

        var config = new ConfigurationBuilder().Build();
        var chatService = new ChatService(db, null!, null!, config);

        // Call the REAL production PersistCitationsForMessageAsync method
        var allowedCandidateSet = new HashSet<int> { chunk.ChunkId };
        var persisted = await chatService.PersistCitationsForMessageAsync(message, [chunk.ChunkId], allowedCandidateSet);
        await db.SaveChangesAsync();

        // 1. Verify safe snippet length <= 2000 and direct version pinning (VersionId = 100, VersionNumber = 1, NOT version 2)
        Assert.Single(persisted);
        var citation = persisted[0];
        Assert.Equal(2000, citation.Snippet.Length);
        Assert.Equal("Original Architecture Document", citation.DocumentTitleSnapshot);
        Assert.Equal(100, citation.DocumentVersionId);
        Assert.Equal(1, citation.VersionNumberSnapshot);
        Assert.Equal(3, citation.PageNumberSnapshot);

        // 2. Immutability verification: Now mutate the original document and chunk
        doc.Title = "Renamed New Document Title";
        chunk.Text = "Completely different chunk content";
        chunk.PageNumber = 99;
        await db.SaveChangesAsync();

        // 3. Confirm citation snapshot remains unchanged
        var reloadedCitation = await db.ChatMessageCitations.AsNoTracking().FirstAsync(c => c.CitationId == citation.CitationId);
        Assert.Equal("Original Architecture Document", reloadedCitation.DocumentTitleSnapshot);
        Assert.Equal(100, reloadedCitation.DocumentVersionId);
        Assert.Equal(1, reloadedCitation.VersionNumberSnapshot);
        Assert.Equal(3, reloadedCitation.PageNumberSnapshot);
        Assert.Equal(2000, reloadedCitation.Snippet.Length);
        Assert.StartsWith("AAAA", reloadedCitation.Snippet);
    }
}
