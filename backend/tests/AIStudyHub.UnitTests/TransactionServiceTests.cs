using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;

namespace AIStudyHub.UnitTests;

public class TransactionServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly TestClock _clock;

    public TransactionServiceTests()
    {
        _factory = new TestDbContextFactory();
        _clock = new TestClock { Now = new DateTime(2025, 1, 15, 10, 0, 0) };

        // Seed required subscription tiers (FKs depend on these)
        SeedSubscriptionTiers().GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    private TransactionService CreateService()
    {
        var context = _factory.CreateContext();
        return new TransactionService(context, _clock);
    }

    private async Task SeedSubscriptionTiers()
    {
        using var ctx = _factory.CreateContext();
        ctx.Subscriptions.AddRange(
            new Subscription { TierId = 1, TierName = "Free", Price = 0m, MaxStorageMb = 50, AiPromptLimitPerDay = 5, TotalStorageMb = 50 },
            new Subscription { TierId = 2, TierName = "Basic", Price = 0m, MaxStorageMb = 200, AiPromptLimitPerDay = 20, TotalStorageMb = 200 },
            new Subscription { TierId = 3, TierName = "Premium", Price = 100_000m, MaxStorageMb = 500, AiPromptLimitPerDay = 100, TotalStorageMb = 500 }
        );
        await ctx.SaveChangesAsync();
    }

    private async Task<User> SeedUser(int balance = 0, int tierId = 2)
    {
        using var ctx = _factory.CreateContext();
        var user = new User
        {
            Username = "testuser",
            Email = $"test{Guid.NewGuid():N}@example.com",
            PasswordHash = "hashed",
            Balance = balance,
            TierId = tierId,
            Role = "STUDENT",
            Status = "ACTIVE"
        };
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user;
    }

    // ─── CreateTransactionAsync ────────────────────────────────

    [Fact]
    public async Task CreateTransaction_WithPositiveAmount_ReturnsTrue()
    {
        var user = await SeedUser();
        var service = CreateService();

        var result = await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = 50_000m,
            Type = "DEPOSIT"
        });

        Assert.True(result);
    }

    [Fact]
    public async Task CreateTransaction_WithZeroAmount_ReturnsFalse()
    {
        var user = await SeedUser();
        var service = CreateService();

        var result = await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = 0,
            Type = "DEPOSIT"
        });

        Assert.False(result);
    }

    [Fact]
    public async Task CreateTransaction_WithNegativeAmount_ReturnsFalse()
    {
        var user = await SeedUser();
        var service = CreateService();

        var result = await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = -100,
            Type = "DEPOSIT"
        });

        Assert.False(result);
    }

    [Fact]
    public async Task CreateTransaction_SetsStatusToPending()
    {
        var user = await SeedUser();
        var service = CreateService();

        await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = 50_000m,
            Type = "DEPOSIT"
        });

        using var ctx = _factory.CreateContext();
        var tx = ctx.Transactions.First();
        Assert.Equal("PENDING", tx.Status);
        Assert.Equal(_clock.Now, tx.StartedAt);
    }

    [Fact]
    public async Task CreateTransaction_NormalizesTypeToUpperCase()
    {
        var user = await SeedUser();
        var service = CreateService();

        await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = 10_000m,
            Type = "deposit"
        });

        using var ctx = _factory.CreateContext();
        var tx = ctx.Transactions.First();
        Assert.Equal("DEPOSIT", tx.Type);
    }

    // ─── BuyPremiumAsync ──────────────────────────────────────

    [Fact]
    public async Task BuyPremium_WithSufficientBalance_Succeeds()
    {
        var user = await SeedUser(balance: 200_000);
        var service = CreateService();

        var result = await service.BuyPremiumAsync(user.UserId);

        Assert.True(result);

        using var ctx = _factory.CreateContext();
        var updatedUser = ctx.Users.First(u => u.UserId == user.UserId);
        Assert.Equal(3, updatedUser.TierId);
        Assert.Equal(100_000, updatedUser.Balance);
        Assert.Equal(_clock.Now.AddDays(30), updatedUser.ExpiresAt);
    }

    [Fact]
    public async Task BuyPremium_WithInsufficientBalance_ReturnsFalse()
    {
        var user = await SeedUser(balance: 50_000);
        var service = CreateService();

        // BuyPremiumAsync catches the InvalidOperationException and returns false
        var result = await service.BuyPremiumAsync(user.UserId);

        Assert.False(result);

        // Balance should be unchanged
        using var ctx = _factory.CreateContext();
        var unchangedUser = ctx.Users.First(u => u.UserId == user.UserId);
        Assert.Equal(50_000, unchangedUser.Balance);
        Assert.NotEqual(3, unchangedUser.TierId);
    }

    [Fact]
    public async Task BuyPremium_CreatesWithdrawTransaction()
    {
        var user = await SeedUser(balance: 200_000);
        var service = CreateService();

        await service.BuyPremiumAsync(user.UserId);

        using var ctx = _factory.CreateContext();
        var tx = ctx.Transactions.First();
        Assert.Equal("WITHDRAW", tx.Type);
        Assert.Equal("SUCCESS", tx.Status);
        Assert.Equal(-100_000m, tx.Amount);
    }

    // ─── GetSubscriptionTiersAsync ────────────────────────────

    [Fact]
    public async Task GetSubscriptionTiers_ReturnsAllTiers()
    {
        var service = CreateService();

        var tiers = await service.GetSubscriptionTiersAsync();

        Assert.Equal(3, tiers.Count);
        Assert.Contains(tiers, t => t.TierName == "Premium" && t.Price == 100_000m);
    }
}
