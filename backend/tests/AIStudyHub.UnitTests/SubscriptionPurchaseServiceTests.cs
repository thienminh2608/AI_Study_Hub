using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIStudyHub.UnitTests;

public class SubscriptionPurchaseServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly TestStudyHubDbContext _db;
    private readonly TestClock _clock;
    private readonly IConfiguration _config;
    private readonly BalanceLedgerService _ledgerService;
    private readonly SubscriptionPurchaseService _purchaseService;

    public SubscriptionPurchaseServiceTests()
    {
        _factory = new TestDbContextFactory();
        _db = _factory.CreateContext();
        _clock = new TestClock
        {
            Now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc),
            UtcNow = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc)
        };

        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Ledger:SecretKey", "TestHmacSecretKey_UnitTests_2026" }
        };
        _config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        _ledgerService = new BalanceLedgerService(_db, _clock, _config, NullLogger<BalanceLedgerService>.Instance);
        _purchaseService = new SubscriptionPurchaseService(_db, _ledgerService, _clock, NullLogger<SubscriptionPurchaseService>.Instance);

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
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Initial_Purchase_Creates_Immutable_Snapshot_With_Initial_Price_And_Entitlements()
    {
        var user = new User
        {
            UserId = 101,
            Username = "student1",
            Email = "student1@test.com",
            Role = "STUDENT",
            Balance = 200_000,
            BalanceVersion = 1,
            TierId = 2
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var result = await _purchaseService.PurchaseTierAsync(101, 3);
        Assert.True(result.Success);
        Assert.Equal("Premium", result.TierName);
        Assert.Equal(100_000m, result.PricePaid);

        // Verify user state
        var updatedUser = await _db.Users.FirstAsync(u => u.UserId == 101);
        Assert.Equal(3, updatedUser.TierId);
        Assert.Equal(100_000, updatedUser.Balance);
        Assert.Equal(_clock.Now.AddDays(30), updatedUser.ExpiresAt);

        // Verify immutable snapshot
        var snapshot = await _db.SubscriptionHistories.FirstOrDefaultAsync(h => h.UserId == 101);
        Assert.NotNull(snapshot);
        Assert.Equal(100_000m, snapshot.PriceSnapshot);
        Assert.Equal("INITIAL_PURCHASE_PRICE", snapshot.PricingPolicySnapshot);
        Assert.Equal("INITIAL_PURCHASE", snapshot.PurchaseType);
        Assert.Equal("USER_BUY", snapshot.ChangeReason);
        Assert.Equal("Premium", snapshot.TierNameSnapshot);
        Assert.Equal(500, snapshot.StorageLimitSnapshot);
        Assert.Equal(100, snapshot.AiPromptLimitSnapshot);
        Assert.Equal(30, snapshot.DurationDaysSnapshot);
        Assert.Equal("VND", snapshot.CurrencySnapshot);
        Assert.NotNull(snapshot.TransactionId);
    }

    [Fact]
    public async Task Changing_Tier_Price_Later_Does_Not_Mutate_Existing_Snapshots()
    {
        var user = new User
        {
            UserId = 102,
            Username = "student2",
            Email = "student2@test.com",
            Role = "STUDENT",
            Balance = 300_000,
            BalanceVersion = 1,
            TierId = 2
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // 1. User buys at 100,000
        var buyResult = await _purchaseService.PurchaseTierAsync(102, 3);
        Assert.True(buyResult.Success);

        // 2. Admin raises tier price to 150,000 and reduces limits
        var tier = await _db.Subscriptions.FirstAsync(s => s.TierId == 3);
        tier.Price = 150_000m;
        tier.MaxStorageMb = 400;
        tier.AiPromptLimitPerDay = 80;
        await _db.SaveChangesAsync();

        // 3. Verify that old snapshot remained unchanged
        var history = await _db.SubscriptionHistories.FirstAsync(h => h.UserId == 102);
        Assert.Equal(100_000m, history.PriceSnapshot);
        Assert.Equal(500, history.StorageLimitSnapshot);
        Assert.Equal(100, history.AiPromptLimitSnapshot);
        Assert.Equal("INITIAL_PURCHASE_PRICE", history.PricingPolicySnapshot);
    }

    [Fact]
    public async Task Auto_Renew_Captures_Current_Price_And_Creates_New_Snapshot()
    {
        var user = new User
        {
            UserId = 103,
            Username = "student3",
            Email = "student3@test.com",
            Role = "STUDENT",
            Balance = 250_000,
            BalanceVersion = 1,
            TierId = 3,
            ExpiresAt = _clock.Now,
            IsAutoRenew = true
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Admin changed price to 120,000 before renewal
        var tier = await _db.Subscriptions.FirstAsync(s => s.TierId == 3);
        tier.Price = 120_000m;
        await _db.SaveChangesAsync();

        var renewed = await _purchaseService.AutoRenewSubscriptionAsync(103, 3);
        Assert.True(renewed);

        var updatedUser = await _db.Users.FirstAsync(u => u.UserId == 103);
        Assert.Equal(130_000, updatedUser.Balance); // 250k - 120k = 130k

        var history = await _db.SubscriptionHistories
            .Where(h => h.UserId == 103)
            .OrderByDescending(h => h.ChangedAt)
            .FirstAsync();

        Assert.Equal(120_000m, history.PriceSnapshot);
        Assert.Equal("STANDARD_CURRENT_TIER_PRICE", history.PricingPolicySnapshot);
        Assert.Equal("AUTO_RENEW", history.PurchaseType);
        Assert.Equal("AUTO_RENEW_SUCCESS", history.ChangeReason);
    }

    [Fact]
    public async Task Downgrade_Creates_Immutable_Downgrade_Snapshot()
    {
        var user = new User
        {
            UserId = 104,
            Username = "student4",
            Email = "student4@test.com",
            Role = "STUDENT",
            Balance = 0,
            BalanceVersion = 1,
            TierId = 3,
            ExpiresAt = _clock.Now
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var downgraded = await _purchaseService.DowngradeUserAsync(104, "GRACE_PERIOD_EXPIRED");
        Assert.True(downgraded);

        var updatedUser = await _db.Users.FirstAsync(u => u.UserId == 104);
        Assert.Equal(2, updatedUser.TierId);
        Assert.Null(updatedUser.ExpiresAt);

        var history = await _db.SubscriptionHistories.FirstAsync(h => h.UserId == 104);
        Assert.Equal(0m, history.PriceSnapshot);
        Assert.Equal("DOWNGRADE_POLICY", history.PricingPolicySnapshot);
        Assert.Equal("DOWNGRADE", history.PurchaseType);
        Assert.Equal("GRACE_PERIOD_EXPIRED", history.ChangeReason);
        Assert.Equal(3, history.OldTierId);
        Assert.Equal(2, history.NewTierId);
    }
}
