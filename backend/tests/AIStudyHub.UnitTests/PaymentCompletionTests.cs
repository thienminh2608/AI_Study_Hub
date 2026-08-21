using System;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIStudyHub.UnitTests;

public class PaymentCompletionTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly TestStudyHubDbContext _db;
    private readonly TestClock _clock;
    private readonly Microsoft.Extensions.Configuration.IConfiguration _config;
    private readonly BalanceLedgerService _ledgerService;
    private readonly PaymentCompletionService _completionService;

    public PaymentCompletionTests()
    {
        _factory = new TestDbContextFactory();
        _db = _factory.CreateContext();
        _clock = new TestClock { Now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc), UtcNow = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc) };
        _config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();
        _ledgerService = new BalanceLedgerService(_db, _clock, _config, NullLogger<BalanceLedgerService>.Instance);
        _completionService = new PaymentCompletionService(_db, _ledgerService, _clock, NullLogger<PaymentCompletionService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
    }

    private async Task<(User User, Transaction Tx)> SeedDepositTransactionAsync(long orderCode = 888888, decimal amount = 50000, string status = "PENDING")
    {
        if (!await _db.Subscriptions.AnyAsync())
        {
            _db.Subscriptions.Add(new Subscription { TierId = 1, TierName = "Free", Price = 0, MaxStorageMb = 50, TotalStorageMb = 50, AiPromptLimitPerDay = 5 });
            await _db.SaveChangesAsync();
        }

        var user = new User
        {
            Username = "payer1",
            Email = "payer@test.com",
            PasswordHash = "hash",
            Role = "STUDENT",
            Status = "ACTIVE",
            TierId = 1,
            Balance = 10000,
            CreatedAt = _clock.Now,
            UpdatedAt = _clock.Now
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var tx = new Transaction
        {
            UserId = user.UserId,
            Amount = amount,
            Type = "DEPOSIT",
            Status = status,
            PayOsOrderCode = orderCode,
            StartedAt = _clock.Now,
            RequiresManualReview = false
        };
        _db.Transactions.Add(tx);
        await _db.SaveChangesAsync();

        return (user, tx);
    }

    [Fact]
    public async Task CompleteDeposit_Success_Credits_Balance_And_Appends_Ledger()
    {
        var (user, tx) = await SeedDepositTransactionAsync(888888, 50000, "PENDING");

        var result = await _completionService.CompleteDepositDirectAsync(
            888888, 50000, "VND", "REF_PAYOS_123", "PAYOS");

        Assert.True(result.Success);
        Assert.False(result.RequiresManualReview);
        Assert.Equal(60000, result.NewBalance); // 10000 + 50000

        _db.ChangeTracker.Clear();
        var updatedTx = await _db.Transactions.AsNoTracking().FirstOrDefaultAsync(t => t.TransactionId == tx.TransactionId);
        Assert.Equal("SUCCESS", updatedTx!.Status);
        Assert.Equal("REF_PAYOS_123", updatedTx.ReferenceCode);

        var updatedUser = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == user.UserId);
        Assert.Equal(60000, updatedUser!.Balance);

        var ledgerEntry = await _db.BalanceLedgers.FirstOrDefaultAsync(l => l.TransactionId == tx.TransactionId);
        Assert.NotNull(ledgerEntry);
        Assert.Equal(10000, ledgerEntry.PreviousBalance);
        Assert.Equal(60000, ledgerEntry.CurrentBalance);
        Assert.Equal(50000, ledgerEntry.Amount);
    }

    [Fact]
    public async Task CompleteDeposit_Idempotent_On_Duplicate_Call()
    {
        var (user, tx) = await SeedDepositTransactionAsync(888888, 50000, "PENDING");

        // 1st call
        var res1 = await _completionService.CompleteDepositDirectAsync(888888, 50000, "VND", "REF_PAYOS_123");
        Assert.True(res1.Success);
        Assert.False(res1.IsDuplicate);

        // 2nd call (duplicate)
        var res2 = await _completionService.CompleteDepositDirectAsync(888888, 50000, "VND", "REF_PAYOS_123");
        Assert.True(res2.Success);
        Assert.True(res2.IsDuplicate);

        // User balance must remain 60,000 (NOT double-credited to 110,000!)
        var updatedUser = await _db.Users.FindAsync(user.UserId);
        Assert.Equal(60000, updatedUser!.Balance);
    }

    [Fact]
    public async Task CompleteDeposit_AmountMismatch_Creates_Reconciliation_Case_And_Flags_ManualReview()
    {
        var (user, tx) = await SeedDepositTransactionAsync(888888, 50000, "PENDING");

        // Webhook arrives with 20,000 instead of expected 50,000
        var res = await _completionService.CompleteDepositDirectAsync(888888, 20000, "VND", "REF_PAYOS_MISMATCH");

        Assert.False(res.Success);
        Assert.True(res.RequiresManualReview);

        // Balance must not change
        var updatedUser = await _db.Users.FindAsync(user.UserId);
        Assert.Equal(10000, updatedUser!.Balance);

        // Transaction must have manual review flagged
        var updatedTx = await _db.Transactions.FindAsync(tx.TransactionId);
        Assert.True(updatedTx!.RequiresManualReview);
        Assert.Equal(50000, updatedTx.ExpectedAmount);
        Assert.Equal(20000, updatedTx.ProviderReportedAmount);

        // Reconciliation case must be persisted
        var reconCase = await _db.PaymentReconciliationCases.FirstOrDefaultAsync(c => c.TransactionId == tx.TransactionId);
        Assert.NotNull(reconCase);
        Assert.Equal("AMOUNT_MISMATCH", reconCase.IssueType);
        Assert.Equal("OPEN", reconCase.Status);
    }

    [Fact]
    public async Task CompleteDeposit_UnknownOrder_Creates_Reconciliation_Case()
    {
        var res = await _completionService.CompleteDepositDirectAsync(99999999, 100000, "VND", "REF_UNKNOWN");

        Assert.False(res.Success);
        Assert.True(res.RequiresManualReview);

        var reconCase = await _db.PaymentReconciliationCases.FirstOrDefaultAsync(c => c.PayOsOrderCode == 99999999);
        Assert.NotNull(reconCase);
        Assert.Equal("UNMATCHED_PAYMENT", reconCase.IssueType);
    }

    [Fact]
    public async Task Concurrent_Deposits_For_Same_User_All_Credit_Correctly()
    {
        var (user, tx1) = await SeedDepositTransactionAsync(111111, 20000, "PENDING");
        user.Balance = 0;
        await _db.SaveChangesAsync();

        var tx2 = new Transaction
        {
            UserId = user.UserId,
            Amount = 30000,
            Type = "DEPOSIT",
            Status = "PENDING",
            PayOsOrderCode = 222222,
            StartedAt = DateTime.UtcNow
        };
        _db.Transactions.Add(tx2);
        await _db.SaveChangesAsync();

        using var db1 = _factory.CreateContext();
        using var db2 = _factory.CreateContext();
        var ledgerService1 = new BalanceLedgerService(db1, _clock, _config, new Microsoft.Extensions.Logging.Abstractions.NullLogger<BalanceLedgerService>());
        var ledgerService2 = new BalanceLedgerService(db2, _clock, _config, new Microsoft.Extensions.Logging.Abstractions.NullLogger<BalanceLedgerService>());
        var service1 = new PaymentCompletionService(db1, ledgerService1, _clock, new Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentCompletionService>());
        var service2 = new PaymentCompletionService(db2, ledgerService2, _clock, new Microsoft.Extensions.Logging.Abstractions.NullLogger<PaymentCompletionService>());

        var task1 = service1.CompleteDepositDirectAsync(111111, 20000, "VND", "REF_1");
        var task2 = service2.CompleteDepositDirectAsync(222222, 30000, "VND", "REF_2");

        var results = await Task.WhenAll(task1, task2);

        Assert.All(results, res => Assert.True(res.Success));

        using var verifyDb = _factory.CreateContext();
        var updatedUser = await verifyDb.Users.FindAsync(user.UserId);
        Assert.Equal(50000, updatedUser!.Balance);

        var completedCount = await verifyDb.Transactions
            .Where(t => t.UserId == user.UserId && t.Status == "SUCCESS")
            .CountAsync();
        Assert.Equal(2, completedCount);

        var ledgers = await verifyDb.BalanceLedgers
            .Where(l => l.UserId == user.UserId)
            .OrderBy(l => l.LedgerSequence)
            .ToListAsync();
        Assert.Equal(2, ledgers.Count);
        Assert.Equal(1, ledgers[0].LedgerSequence);
        Assert.Equal(2, ledgers[1].LedgerSequence);
    }

    [Fact]
    public async Task WebhookEvent_Marked_Processed_Even_When_ChangeTracker_Cleared()
    {
        var (user, tx) = await SeedDepositTransactionAsync(999111, 50000, "PENDING");

        var payload = new AIStudyHub.Application.Interfaces.PayOsWebhookPayloadDto
        {
            Code = "00",
            Desc = "success",
            Data = new PayOsWebhookDataDto
            {
                OrderCode = 999111,
                Amount = 50000,
                Description = "Nap vi 999111",
                AccountNumber = "0123456789",
                Reference = "REF_OCC_CLEAR",
                TransactionDateTime = "2026-08-20 12:00:00",
                Currency = "VND",
                PaymentLinkId = "link_occ_clear",
                Code = "00",
                Desc = "success"
            }
        };

        var result = await _completionService.ProcessWebhookTransactionallyAsync(
            payload,
            "{\"mock\":\"sanitized\"}",
            "mock_payload_hash");

        Assert.True(result.Success);

        _db.ChangeTracker.Clear();
        var webhookEvent = await _db.PaymentWebhookEvents.FirstOrDefaultAsync(e => e.ProviderEventId == "REF_OCC_CLEAR");
        Assert.NotNull(webhookEvent);
        Assert.Equal("PROCESSED", webhookEvent.Status);
    }

    [Fact]
    public async Task Ledger_Collision_Rolls_Back_To_Savepoint_Retries_And_Marks_Webhook_Processed()
    {
        var (user, tx) = await SeedDepositTransactionAsync(999222, 100000, "PENDING");
        user.Balance = 10000;
        await _db.SaveChangesAsync();

        int ledgerCallCount = 0;
        var faultInjectingLedgerService = new MockFaultLedgerService(_ledgerService, () =>
        {
            ledgerCallCount++;
            if (ledgerCallCount == 1)
            {
                // Inject duplicate entity that causes DbUpdateException on SaveChangesAsync
                var collisionLedger = new BalanceLedger
                {
                    UserId = user.UserId,
                    LedgerSequence = 1,
                    PreviousHash = "GENESIS",
                    CurrentHash = "COLLISION_HASH",
                    ActionType = "DEPOSIT",
                    Amount = 100000,
                    PreviousBalance = 10000,
                    CurrentBalance = 110000,
                    CreatedAtUtc = DateTime.UtcNow
                };
                // Pre-insert with same sequence to guarantee UNIQUE collision on SaveChangesAsync
                _db.BalanceLedgers.Add(collisionLedger);
            }
        });

        var completionService = new PaymentCompletionService(_db, faultInjectingLedgerService, _clock, NullLogger<PaymentCompletionService>.Instance);

        var payload = new AIStudyHub.Application.Interfaces.PayOsWebhookPayloadDto
        {
            Code = "00",
            Desc = "success",
            Data = new PayOsWebhookDataDto
            {
                OrderCode = 999222,
                Amount = 100000,
                Description = "Nap vi 999222",
                AccountNumber = "0123456789",
                Reference = "REF_COLLISION_RETRY",
                TransactionDateTime = "2026-08-20 12:00:00",
                Currency = "VND",
                PaymentLinkId = "link_collision",
                Code = "00",
                Desc = "success"
            }
        };

        var result = await completionService.ProcessWebhookTransactionallyAsync(
            payload,
            "{\"mock\":\"sanitized\"}",
            "mock_payload_hash_collision");

        Assert.True(result.Success);
        Assert.True(ledgerCallCount >= 2, "Ledger service should have been retried after collision.");

        _db.ChangeTracker.Clear();
        var updatedUser = await _db.Users.FindAsync(user.UserId);
        Assert.Equal(110000, updatedUser!.Balance); // 10,000 + 100,000 exactly ONCE

        var updatedTx = await _db.Transactions.FindAsync(tx.TransactionId);
        Assert.Equal("SUCCESS", updatedTx!.Status);

        var webhookEvent = await _db.PaymentWebhookEvents.FirstOrDefaultAsync(e => e.ProviderEventId == "REF_COLLISION_RETRY");
        Assert.NotNull(webhookEvent);
        Assert.Equal("PROCESSED", webhookEvent.Status);
        Assert.Equal(100000, webhookEvent.ReceivedAmount);
        Assert.Equal(100000, webhookEvent.ExpectedAmount);

        var validLedgers = await _db.BalanceLedgers.Where(l => l.UserId == user.UserId).ToListAsync();
        Assert.Single(validLedgers);
        Assert.Equal(110000, validLedgers[0].CurrentBalance);
    }

    private class MockFaultLedgerService : IBalanceLedgerService
    {
        private readonly IBalanceLedgerService _inner;
        private readonly Action _onBeforeAppend;

        public MockFaultLedgerService(IBalanceLedgerService inner, Action onBeforeAppend)
        {
            _inner = inner;
            _onBeforeAppend = onBeforeAppend;
        }

        public async Task<BalanceLedger> AppendEntryAsync(
            int userId,
            int? transactionId,
            decimal amount,
            decimal prevBalance,
            decimal currBalance,
            string actionType,
            string? description,
            System.Threading.CancellationToken cancellationToken = default)
        {
            _onBeforeAppend();
            return await _inner.AppendEntryAsync(userId, transactionId, amount, prevBalance, currBalance, actionType, description, cancellationToken);
        }

        public Task<LedgerVerificationResult> VerifyChainIntegrityAsync(int userId, System.Threading.CancellationToken cancellationToken = default)
            => _inner.VerifyChainIntegrityAsync(userId, cancellationToken);

        public string ComputeCurrentHash(
            int hashVersion,
            int keyVersion,
            string previousHash,
            int userId,
            long sequence,
            int? transactionId,
            decimal amount,
            decimal prevBalance,
            decimal currBalance,
            string actionType,
            DateTime createdAtUtc)
            => _inner.ComputeCurrentHash(hashVersion, keyVersion, previousHash, userId, sequence, transactionId, amount, prevBalance, currBalance, actionType, createdAtUtc);
    }
}
