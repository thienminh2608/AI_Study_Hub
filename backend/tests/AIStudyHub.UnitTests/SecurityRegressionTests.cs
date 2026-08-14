using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Services.Gemini;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.UnitTests;

public class SecurityRegressionTests
{
    [Fact]
    public async Task Chat_DoesNotAllowWritingToAnotherUsersSession()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        db.Subscriptions.Add(CreateFreeTier());
        db.Users.AddRange(CreateUser(1), CreateUser(2));
        db.ChatSessions.Add(new ChatSession { SessionId = 10, UserId = 2, SessionName = "private" });
        await db.SaveChangesAsync();

        var service = new ChatService(db, new MockGeminiService());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            service.ProcessUserMessageAsync(1, 10, new AskQuestionDto { MessageContent = "hello" }));
        Assert.Empty(db.ChatMessages);
    }

    [Fact]
    public async Task Friendship_RequesterCannotAcceptOwnRequest()
    {
        using var factory = new TestDbContextFactory();
        await using var db = factory.CreateContext();
        db.Subscriptions.Add(CreateFreeTier());
        db.Users.AddRange(CreateUser(1), CreateUser(2));
        db.Friendships.Add(new Friendship
        {
            RequesterId = 1,
            AddresseeId = 2,
            Status = "PENDING"
        });
        await db.SaveChangesAsync();

        var service = new FriendshipService(db);

        Assert.False(await service.UpdateFriendshipStatusAsync(1, 2, "ACCEPTED"));
        Assert.True(await service.UpdateFriendshipStatusAsync(2, 1, "ACCEPTED"));
        var notice = await db.ModerationNotices.SingleAsync();
        Assert.Equal(1, notice.UserId);
        Assert.Equal(2, notice.RelatedUserId);
        Assert.Equal("FRIEND_ACCEPTED", notice.Type);
    }

    private static User CreateUser(int id) => new()
    {
        UserId = id,
        Username = $"user{id}",
        Email = $"user{id}@example.com",
        PasswordHash = "hash",
        Role = "STUDENT",
        Status = "ACTIVE",
        TierId = 2
    };

    private static Subscription CreateFreeTier() => new()
    {
        TierId = 2,
        TierName = "Free",
        MaxStorageMb = 50,
        AiPromptLimitPerDay = 10,
        TotalStorageMb = 50,
        Price = 0
    };
}
