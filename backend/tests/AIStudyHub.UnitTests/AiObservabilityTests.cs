using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Services.Gemini;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AIStudyHub.UnitTests;

public class AiObservabilityTests
{
    [Fact]
    public async Task MockGeminiService_ReturnsStructuredTokenMetadata()
    {
        // Arrange
        var gemini = new MockGeminiService();
        var history = new List<ChatMessageDto>
        {
            new() { Sender = "USER", MessageContent = "Giải thích tính đa hình trong OOP" }
        };

        // Act
        var result = await gemini.GetGeminiResponseAsync(history, "CHAT", CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Contains("đa hình", result.Content, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.PromptTokens > 0);
        Assert.True(result.CompletionTokens > 0);
        Assert.Equal(result.TotalTokens, result.PromptTokens + result.CompletionTokens);
        Assert.True(result.EstimatedCost > 0);
        Assert.Equal("SUCCESS", result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.RequestId));
    }

    [Fact]
    public async Task ChatService_ProcessUserMessage_RecordsAiUsageObservabilityMetrics()
    {
        // Arrange
        using var factory = new TestDbContextFactory();
        await using var dbContext = factory.CreateContext();

        var tier = new Subscription
        {
            TierId = 2,
            TierName = "Free",
            AiPromptLimitPerDay = 50,
            MaxStorageMb = 100,
            TotalStorageMb = 100,
            Price = 0
        };
        var user = new User
        {
            UserId = 10,
            Username = "student1",
            Email = "student1@test.com",
            PasswordHash = "hash",
            Role = "STUDENT",
            Status = "ACTIVE",
            TierId = 2,
            AiPromptsToday = 0
        };
        dbContext.Subscriptions.Add(tier);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        var session = new ChatSession
        {
            SessionId = 100,
            UserId = 10,
            SessionName = "Study Session",
            CreatedAt = DateTime.UtcNow
        };
        dbContext.ChatSessions.Add(session);
        await dbContext.SaveChangesAsync();

        var geminiMock = new MockGeminiService();
        var config = new ConfigurationBuilder().Build();
        var permissionService = new PermissionService(dbContext);
        var chatService = new ChatService(dbContext, geminiMock, permissionService, config);

        // Act
        var response = await chatService.ProcessUserMessageAsync(10, 100, new AskQuestionDto
        {
            MessageContent = "Giải thích tính đa hình trong OOP"
        });

        // Assert
        Assert.NotNull(response);
        
        // Verify AiUsage was recorded
        var usages = await dbContext.AiUsages.Where(u => u.UserId == 10).ToListAsync();
        Assert.NotEmpty(usages);

        var lastUsage = usages.Last();
        Assert.Equal(10, lastUsage.UserId);
        Assert.Equal("CHAT", lastUsage.Operation);
        Assert.True(lastUsage.PromptTokens > 0);
        Assert.True(lastUsage.CompletionTokens > 0);
        Assert.True(lastUsage.TotalTokens > 0);
        Assert.Equal("SUCCESS", lastUsage.Status);
        Assert.True(lastUsage.EstimatedCost > 0);
        Assert.False(string.IsNullOrWhiteSpace(lastUsage.RequestId));
    }

    [Fact]
    public async Task AiUsage_CalculatesAggregatesCorrectly()
    {
        // Arrange
        using var factory = new TestDbContextFactory();
        await using var dbContext = factory.CreateContext();

        var tier = new Subscription
        {
            TierId = 2,
            TierName = "Free",
            AiPromptLimitPerDay = 50,
            MaxStorageMb = 100,
            TotalStorageMb = 100,
            Price = 0
        };
        var user = new User
        {
            UserId = 20,
            Username = "student2",
            Email = "student2@test.com",
            PasswordHash = "hash",
            Role = "STUDENT",
            Status = "ACTIVE",
            TierId = 2
        };
        dbContext.Subscriptions.Add(tier);
        dbContext.Users.Add(user);

        dbContext.AiUsages.AddRange(
            new AiUsage
            {
                UserId = 20,
                Provider = "Google",
                Model = "gemini-3.1-flash-lite",
                Operation = "CHAT",
                PromptTokens = 100,
                CompletionTokens = 50,
                TotalTokens = 150,
                LatencyMs = 200,
                EstimatedCost = 0.0000225m,
                Status = "SUCCESS",
                RequestId = Guid.NewGuid().ToString()
            },
            new AiUsage
            {
                UserId = 20,
                Provider = "Google",
                Model = "gemini-3.1-flash-lite",
                Operation = "DOCUMENT_SUMMARY",
                PromptTokens = 500,
                CompletionTokens = 100,
                TotalTokens = 600,
                LatencyMs = 450,
                EstimatedCost = 0.0000675m,
                Status = "SUCCESS",
                RequestId = Guid.NewGuid().ToString()
            }
        );
        await dbContext.SaveChangesAsync();

        // Act
        var totalRequests = await dbContext.AiUsages.CountAsync();
        var totalTokens = await dbContext.AiUsages.SumAsync(u => u.TotalTokens);
        var totalCost = await dbContext.AiUsages.SumAsync(u => u.EstimatedCost);

        // Assert
        Assert.Equal(2, totalRequests);
        Assert.Equal(750, totalTokens);
        Assert.Equal(0.00009m, totalCost);
    }
}
