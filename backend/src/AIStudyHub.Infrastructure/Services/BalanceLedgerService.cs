using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Infrastructure.Services;

public class BalanceLedgerService : IBalanceLedgerService
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BalanceLedgerService> _logger;

    private const int CurrentHashVersion = 1;
    private const int CurrentKeyVersion = 1;
    private const string DefaultDevKey = "AIStudyHubSecureLedgerKey_DevelopmentFallback_2026";

    public BalanceLedgerService(
        IStudyHubDbContext dbContext,
        IClock clock,
        IConfiguration configuration,
        ILogger<BalanceLedgerService> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _configuration = configuration;
        _logger = logger;
    }

    private string GetSecretKey(int keyVersion)
    {
        // Support per-key version configuration e.g. Ledger:SecretKey_v1 or fallback to Ledger:SecretKey
        string? key = _configuration[$"Ledger:SecretKey_v{keyVersion}"] 
                   ?? _configuration["Ledger:SecretKey"]
                   ?? Environment.GetEnvironmentVariable($"LEDGER_SECRET_KEY_V{keyVersion}")
                   ?? Environment.GetEnvironmentVariable("LEDGER_SECRET_KEY");

        if (string.IsNullOrWhiteSpace(key))
        {
            _logger.LogWarning("Ledger SecretKey is not configured. Falling back to default development key.");
            return DefaultDevKey;
        }

        return key;
    }

    public async Task<BalanceLedger> AppendEntryAsync(
        int userId,
        int? transactionId,
        decimal amount,
        decimal prevBalance,
        decimal currBalance,
        string actionType,
        string? description,
        CancellationToken cancellationToken = default)
    {
        // Query the latest entry for this user to establish the hash chain
        var lastEntry = await _dbContext.BalanceLedgers
            .Where(l => l.UserId == userId)
            .OrderByDescending(l => l.LedgerSequence)
            .FirstOrDefaultAsync(cancellationToken);

        long nextSequence = (lastEntry?.LedgerSequence ?? 0) + 1;
        string previousHash = lastEntry?.CurrentHash ?? "GENESIS";
        DateTime createdAtUtc = _clock.Now.Kind == DateTimeKind.Utc ? _clock.Now : _clock.Now.ToUniversalTime();

        string currentHash = ComputeCurrentHash(
            CurrentHashVersion,
            CurrentKeyVersion,
            previousHash,
            userId,
            nextSequence,
            transactionId,
            amount,
            prevBalance,
            currBalance,
            actionType,
            createdAtUtc);

        var ledger = new BalanceLedger
        {
            UserId = userId,
            LedgerSequence = nextSequence,
            TransactionId = transactionId,
            Amount = amount,
            PreviousBalance = prevBalance,
            CurrentBalance = currBalance,
            ActionType = actionType,
            Description = description,
            PreviousHash = previousHash,
            CurrentHash = currentHash,
            HashVersion = CurrentHashVersion,
            KeyVersion = CurrentKeyVersion,
            CreatedAtUtc = createdAtUtc
        };

        _dbContext.BalanceLedgers.Add(ledger);
        return ledger;
    }

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
    {
        string txIdStr = transactionId.HasValue ? transactionId.Value.ToString(CultureInfo.InvariantCulture) : "";
        string amountStr = amount.ToString("F2", CultureInfo.InvariantCulture);
        string prevBalanceStr = prevBalance.ToString("F2", CultureInfo.InvariantCulture);
        string currBalanceStr = currBalance.ToString("F2", CultureInfo.InvariantCulture);
        string timeStr = createdAtUtc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

        string canonicalPayload = $"{hashVersion}|{keyVersion}|{previousHash}|{userId}|{sequence}|{txIdStr}|{amountStr}|{prevBalanceStr}|{currBalanceStr}|{actionType}|{timeStr}";
        string secretKey = GetSecretKey(keyVersion);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secretKey));
        byte[] hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    public async Task<LedgerVerificationResult> VerifyChainIntegrityAsync(int userId, CancellationToken cancellationToken = default)
    {
        var entries = await _dbContext.BalanceLedgers
            .Where(l => l.UserId == userId)
            .OrderBy(l => l.LedgerSequence)
            .ToListAsync(cancellationToken);

        var user = await _dbContext.Users.FindAsync(new object?[] { userId }, cancellationToken);
        decimal currentUserBalance = user?.Balance ?? 0m;

        var result = new LedgerVerificationResult
        {
            TotalEntries = entries.Count,
            CurrentUserBalance = currentUserBalance
        };

        if (entries.Count == 0)
        {
            result.IsValid = true;
            result.CalculatedClosingBalance = 0m;
            return result;
        }

        string expectedPrevHash = "GENESIS";
        long expectedSequence = 1;

        for (int i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];

            // 1. Check sequence ordering
            if (entry.LedgerSequence != expectedSequence)
            {
                result.IsValid = false;
                result.FailedSequence = entry.LedgerSequence;
                result.FailedReason = $"Sequence gap or disorder. Expected {expectedSequence}, got {entry.LedgerSequence}.";
                return result;
            }

            // 2. Check PreviousHash linkage
            if (!string.Equals(entry.PreviousHash, expectedPrevHash, StringComparison.OrdinalIgnoreCase))
            {
                result.IsValid = false;
                result.FailedSequence = entry.LedgerSequence;
                result.FailedReason = $"Broken chain linkage at sequence {entry.LedgerSequence}. Expected prev hash {expectedPrevHash}, got {entry.PreviousHash}.";
                return result;
            }

            // 3. Recompute and verify CurrentHash (HMAC-SHA256)
            string recomputedHash = ComputeCurrentHash(
                entry.HashVersion,
                entry.KeyVersion,
                entry.PreviousHash,
                entry.UserId,
                entry.LedgerSequence,
                entry.TransactionId,
                entry.Amount,
                entry.PreviousBalance,
                entry.CurrentBalance,
                entry.ActionType,
                entry.CreatedAtUtc);

            if (!string.Equals(entry.CurrentHash, recomputedHash, StringComparison.OrdinalIgnoreCase))
            {
                result.IsValid = false;
                result.FailedSequence = entry.LedgerSequence;
                result.FailedReason = $"Signature mismatch at sequence {entry.LedgerSequence}. Entry may have been tampered with.";
                return result;
            }

            // 4. Arithmetic balance integrity check
            decimal expectedCurr = entry.PreviousBalance + entry.Amount;
            if (entry.CurrentBalance != expectedCurr && entry.ActionType != "OPENING_BALANCE")
            {
                result.IsValid = false;
                result.FailedSequence = entry.LedgerSequence;
                result.FailedReason = $"Balance arithmetic mismatch at sequence {entry.LedgerSequence}. Expected {expectedCurr}, recorded {entry.CurrentBalance}.";
                return result;
            }

            expectedPrevHash = entry.CurrentHash;
            expectedSequence++;
            result.VerifiedEntries++;
        }

        result.CalculatedClosingBalance = entries.Last().CurrentBalance;
        
        // 5. Compare closing balance with actual user balance
        if (result.CalculatedClosingBalance != currentUserBalance)
        {
            result.IsValid = false;
            result.FailedReason = $"Ledger closing balance ({result.CalculatedClosingBalance:N2}) does not match User balance ({currentUserBalance:N2}).";
            return result;
        }

        result.IsValid = true;
        return result;
    }
}
