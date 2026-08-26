using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIStudyHub.UnitTests;

public class TransactionServiceTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly TestClock _clock;
    private readonly IConfiguration _config;

    public TransactionServiceTests()
    {
        _factory = new TestDbContextFactory();
        _clock = new TestClock { Now = new DateTime(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc) };
        
        var inMemorySettings = new Dictionary<string, string?> {
            {"Ledger:SecretKey", "TestHmacSecretKey_UnitTests_2026"}
        };
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        // Seed required subscription tiers (FKs depend on these)
        SeedSubscriptionTiers().GetAwaiter().GetResult();
    }

    public void Dispose() => _factory.Dispose();

    private (TransactionService Service, IBalanceLedgerService LedgerService, Infrastructure.Persistence.StudyHubDbContext Context) CreateServices(Infrastructure.Persistence.StudyHubDbContext? existingContext = null)
    {
        var context = existingContext ?? _factory.CreateContext();
        var ledgerService = new BalanceLedgerService(context, _clock, _config, NullLogger<BalanceLedgerService>.Instance);
        var subPurchaseService = new SubscriptionPurchaseService(context, ledgerService, _clock, NullLogger<SubscriptionPurchaseService>.Instance);
        var txService = new TransactionService(context, _clock, ledgerService, subPurchaseService);
        return (txService, ledgerService, context);
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
        var (service, _, _) = CreateServices();

        var result = await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = 50_000m,
            Type = "DEPOSIT",
            BankId = "VCB",
            ReferenceCode = "REF-VALID-1"
        });

        Assert.True(result);
    }

    [Fact]
    public async Task CreateTransaction_WithZeroAmount_ReturnsFalse()
    {
        var user = await SeedUser();
        var (service, _, _) = CreateServices();

        var result = await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = 0,
            Type = "DEPOSIT",
            BankId = "VCB",
            ReferenceCode = "REF-ZERO"
        });

        Assert.False(result);
    }

    [Fact]
    public async Task CreateTransaction_WithNegativeAmount_ReturnsFalse()
    {
        var user = await SeedUser();
        var (service, _, _) = CreateServices();

        var result = await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = -100,
            Type = "DEPOSIT",
            BankId = "VCB",
            ReferenceCode = "REF-NEGATIVE"
        });

        Assert.False(result);
    }

    [Fact]
    public async Task CreateTransaction_WithAmountBelowMinimum_ReturnsFalse()
    {
        var user = await SeedUser();
        var (service, _, _) = CreateServices();

        var result = await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = 1_999m,
            Type = "DEPOSIT",
            BankId = "VCB",
            ReferenceCode = "REF-BELOW-MINIMUM"
        });

        Assert.False(result);
    }

    [Theory]
    [InlineData("", "REF-VALID")]
    [InlineData("VCB", "")]
    public async Task CreateTransaction_WithMissingReconciliationData_ReturnsFalse(string bankId, string referenceCode)
    {
        var user = await SeedUser();
        var (service, _, _) = CreateServices();

        var result = await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = 50_000m,
            Type = "DEPOSIT",
            BankId = bankId,
            ReferenceCode = referenceCode
        });

        Assert.False(result);
    }

    [Fact]
    public async Task CreateTransaction_SetsStatusToPending()
    {
        var user = await SeedUser();
        var (service, _, _) = CreateServices();

        await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto
        {
            Amount = 50_000m,
            Type = "DEPOSIT",
            BankId = "VCB",
            ReferenceCode = "REF-PENDING"
        });

        using var ctx = _factory.CreateContext();
        var tx = ctx.Transactions.First();
        Assert.Equal("PENDING", tx.Status);
        Assert.Equal(_clock.Now, tx.StartedAt);
    }

    [Fact]
    public async Task ApproveDeposit_WhenAlreadyProcessed_DoesNotCreditTwice()
    {
        var user = await SeedUser();
        var (service, _, _) = CreateServices();
        await service.CreateTransactionAsync(user.UserId, new CreateTransactionDto { Amount = 50_000m, Type = "DEPOSIT", BankId = "VCB", ReferenceCode = "REF-APPROVE" });
        int transactionId;
        using (var ctx = _factory.CreateContext()) transactionId = ctx.Transactions.Single().TransactionId;

        Assert.True(await service.UpdateTransactionStatusAsync(transactionId, "SUCCESS", 1, null));
        Assert.False(await service.UpdateTransactionStatusAsync(transactionId, "SUCCESS", 1, null));

        using var verify = _factory.CreateContext();
        Assert.Equal(50_000, verify.Users.Single(u => u.UserId == user.UserId).Balance);
        Assert.Single(verify.BalanceLedgers.Where(l => l.TransactionId == transactionId));
    }

    [Fact]
    public async Task RefundTransaction_ForDeposit_IsRejected()
    {
        var user = await SeedUser(balance: 50_000);
        using (var ctx = _factory.CreateContext())
        {
            ctx.Transactions.Add(new Transaction { UserId = user.UserId, Amount = 50_000m, Type = "DEPOSIT", Status = "SUCCESS", StartedAt = _clock.Now, CompletedAt = _clock.Now });
            await ctx.SaveChangesAsync();
        }
        int transactionId;
        using (var ctx = _factory.CreateContext()) transactionId = ctx.Transactions.Single().TransactionId;

        var (service, _, _) = CreateServices();
        Assert.False(await service.RefundTransactionAsync(transactionId, 1, "invalid reversal"));

        using var verify = _factory.CreateContext();
        Assert.Equal(50_000, verify.Users.Single(u => u.UserId == user.UserId).Balance);
        Assert.DoesNotContain(verify.Transactions, t => t.Type == "REFUND" || t.Type == "REFUND_PURCHASE");
    }

    // ─── ReverseDepositAsync ───────────────────────────────────

    [Fact]
    public async Task ReverseDeposit_WithSufficientBalance_DeductsWalletAndAppendsLedger()
    {
        var user = await SeedUser(balance: 50_000);
        int depositTxId;
        using (var ctx = _factory.CreateContext())
        {
            var depositTx = new Transaction { UserId = user.UserId, Amount = 50_000m, Type = "DEPOSIT", Status = "SUCCESS", StartedAt = _clock.Now, CompletedAt = _clock.Now };
            ctx.Transactions.Add(depositTx);
            await ctx.SaveChangesAsync();
            depositTxId = depositTx.TransactionId;
        }

        var (service, ledgerService, _) = CreateServices();
        var success = await service.ReverseDepositAsync(depositTxId, 1, "Chuyển khoản nhầm, hoàn tiền");
        Assert.True(success);

        using var verify = _factory.CreateContext();
        var updatedUser = verify.Users.Single(u => u.UserId == user.UserId);
        Assert.Equal(0, updatedUser.Balance);

        var revTx = verify.Transactions.Single(t => t.Type == "REVERSE_DEPOSIT");
        Assert.Equal(-50_000m, revTx.Amount);
        Assert.Equal(depositTxId, revTx.OriginalTransactionId);

        var verifyResult = await ledgerService.VerifyChainIntegrityAsync(user.UserId);
        Assert.True(verifyResult.IsValid);
        Assert.Equal(0m, verifyResult.CalculatedClosingBalance);
    }

    [Fact]
    public async Task ReverseDeposit_WithInsufficientBalance_IsRejected()
    {
        var user = await SeedUser(balance: 10_000); // Balance is less than deposit amount 50_000
        int depositTxId;
        using (var ctx = _factory.CreateContext())
        {
            var depositTx = new Transaction { UserId = user.UserId, Amount = 50_000m, Type = "DEPOSIT", Status = "SUCCESS", StartedAt = _clock.Now, CompletedAt = _clock.Now };
            ctx.Transactions.Add(depositTx);
            await ctx.SaveChangesAsync();
            depositTxId = depositTx.TransactionId;
        }

        var (service, _, _) = CreateServices();
        var success = await service.ReverseDepositAsync(depositTxId, 1, "Chuyển khoản nhầm");
        Assert.False(success);

        using var verify = _factory.CreateContext();
        Assert.Equal(10_000, verify.Users.Single(u => u.UserId == user.UserId).Balance);
        Assert.DoesNotContain(verify.Transactions, t => t.Type == "REVERSE_DEPOSIT");
    }

    [Fact]
    public async Task ReverseDeposit_WhenAlreadyReversed_IsRejected()
    {
        var user = await SeedUser(balance: 100_000);
        int depositTxId;
        using (var ctx = _factory.CreateContext())
        {
            var depositTx = new Transaction { UserId = user.UserId, Amount = 50_000m, Type = "DEPOSIT", Status = "SUCCESS", StartedAt = _clock.Now, CompletedAt = _clock.Now };
            ctx.Transactions.Add(depositTx);
            await ctx.SaveChangesAsync();
            depositTxId = depositTx.TransactionId;
        }

        var (service, _, _) = CreateServices();
        Assert.True(await service.ReverseDepositAsync(depositTxId, 1, "Reversal 1"));
        Assert.False(await service.ReverseDepositAsync(depositTxId, 1, "Reversal 2"));

        using var verify = _factory.CreateContext();
        Assert.Equal(50_000, verify.Users.Single(u => u.UserId == user.UserId).Balance);
    }

    // ─── BalanceLedger Integrity & Tampering ───────────────────

    [Fact]
    public async Task Ledger_VerifyChainIntegrity_DetectsTampering()
    {
        var user = await SeedUser(balance: 100_000);
        using var ctx = _factory.CreateContext();
        var (_, ledgerService, _) = CreateServices(ctx);

        await ledgerService.AppendEntryAsync(user.UserId, null, 100_000m, 0m, 100_000m, "OPENING_BALANCE", "Deposit initial");
        await ctx.SaveChangesAsync();

        // 1. Valid before tampering
        var validResult = await ledgerService.VerifyChainIntegrityAsync(user.UserId);
        Assert.True(validResult.IsValid);

        // 2. Tamper an entry in the DB directly
        using (var tamperCtx = _factory.CreateContext())
        {
            var entry = tamperCtx.BalanceLedgers.First(l => l.UserId == user.UserId);
            entry.Amount = 999_999m; // Tampered amount without recalculating HMAC signature
            await tamperCtx.SaveChangesAsync();
        }

        // 3. Re-verify - must detect tampering
        using var checkCtx = _factory.CreateContext();
        var (_, checkLedger, _) = CreateServices(checkCtx);
        var tamperedResult = await checkLedger.VerifyChainIntegrityAsync(user.UserId);
        Assert.False(tamperedResult.IsValid);
        Assert.Contains("Signature mismatch", tamperedResult.FailedReason);
    }

    // ─── BuyPremiumAsync ──────────────────────────────────────

    [Fact]
    public async Task BuyPremium_WithSufficientBalance_Succeeds()
    {
        var user = await SeedUser(balance: 200_000);
        var (service, ledgerService, _) = CreateServices();

        var result = await service.BuyPremiumAsync(user.UserId);

        Assert.True(result);

        using var ctx = _factory.CreateContext();
        var updatedUser = ctx.Users.First(u => u.UserId == user.UserId);
        Assert.Equal(3, updatedUser.TierId);
        Assert.Equal(100_000, updatedUser.Balance);
        Assert.Equal(_clock.Now.AddDays(30), updatedUser.ExpiresAt);

        var verifyResult = await ledgerService.VerifyChainIntegrityAsync(user.UserId);
        Assert.True(verifyResult.IsValid);
    }

    [Fact]
    public async Task BuyPremium_WithInsufficientBalance_ReturnsFalse()
    {
        var user = await SeedUser(balance: 50_000);
        var (service, _, _) = CreateServices();

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
        var (service, _, _) = CreateServices();

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
        var (service, _, _) = CreateServices();

        var tiers = await service.GetSubscriptionTiersAsync();

        Assert.Equal(3, tiers.Count);
        Assert.Contains(tiers, t => t.TierName == "Premium" && t.Price == 100_000m);
    }
}
