using System;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Domain.Entities;

namespace AIStudyHub.Application.Interfaces;

public class LedgerVerificationResult
{
    public bool IsValid { get; set; }
    public int TotalEntries { get; set; }
    public int VerifiedEntries { get; set; }
    public string? FailedReason { get; set; }
    public long? FailedSequence { get; set; }
    public decimal CalculatedClosingBalance { get; set; }
    public decimal CurrentUserBalance { get; set; }
}

public interface IBalanceLedgerService
{
    Task<BalanceLedger> AppendEntryAsync(
        int userId,
        int? transactionId,
        decimal amount,
        decimal prevBalance,
        decimal currBalance,
        string actionType,
        string? description,
        CancellationToken cancellationToken = default);

    Task<LedgerVerificationResult> VerifyChainIntegrityAsync(int userId, CancellationToken cancellationToken = default);

    string ComputeCurrentHash(
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
        DateTime createdAtUtc);
}
