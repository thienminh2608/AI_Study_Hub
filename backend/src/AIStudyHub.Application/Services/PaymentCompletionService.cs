using System;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Application.Services;

public class PaymentCompletionService : IPaymentCompletionService
{
    private readonly IStudyHubDbContext _db;
    private readonly IBalanceLedgerService _ledgerService;
    private readonly IClock _clock;
    private readonly ILogger<PaymentCompletionService> _logger;

    public PaymentCompletionService(
        IStudyHubDbContext db,
        IBalanceLedgerService ledgerService,
        IClock clock,
        ILogger<PaymentCompletionService> logger)
    {
        _db = db;
        _ledgerService = ledgerService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<PaymentCompletionResult> ProcessWebhookTransactionallyAsync(
        PayOsWebhookPayloadDto payload,
        string sanitizedPayloadJson,
        string payloadHash,
        CancellationToken cancellationToken = default)
    {
        if (payload?.Data == null)
        {
            return new PaymentCompletionResult { Success = false, Message = "Empty webhook data." };
        }

        var data = payload.Data;
        var now = _clock.UtcNow;

        var reference = data.Reference?.Trim();
        bool isSynthetic = false;
        if (string.IsNullOrWhiteSpace(reference))
        {
            var composite = $"PAYOS:{data.PaymentLinkId}:{data.OrderCode}:{data.Amount}:{data.TransactionDateTime}";
            var compositeHash = SHA256.HashData(Encoding.UTF8.GetBytes(composite));
            reference = $"SYNTH_{Convert.ToHexString(compositeHash).ToLowerInvariant()}";
            isSynthetic = true;
        }

        var webhookEvent = new PaymentWebhookEvent
        {
            Provider = "PAYOS",
            ProviderEventId = reference,
            MerchantOrderCode = data.OrderCode,
            PayloadHash = payloadHash,
            PayloadSanitized = sanitizedPayloadJson,
            ReceivedAmount = data.Amount,
            Currency = data.Currency ?? "VND",
            IsSyntheticReference = isSynthetic,
            ProcessedAt = now,
            Status = "RECEIVED",
            CreatedAt = now
        };

        var strategy = _db.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

            // 1. Insert webhook event (Status = RECEIVED) with duplicate constraint handling
            try
            {
                _db.PaymentWebhookEvents.Add(webhookEvent);
                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                await tx.RollbackAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                _logger.LogInformation("Duplicate PayOS webhook reference {Reference} received. Idempotent return OK.", reference);
                return new PaymentCompletionResult
                {
                    Success = true,
                    IsDuplicate = true,
                    Message = "Webhook event already processed."
                };
            }

            // 2. Locate and Validate Transaction
            var transaction = await _db.Transactions
                .FirstOrDefaultAsync(t => t.PayOsOrderCode == data.OrderCode, cancellationToken);

            if (transaction == null)
            {
                _logger.LogWarning("Payment received for unknown order code {OrderCode}, reference {Reference}", data.OrderCode, reference);

                var reconCase = new PaymentReconciliationCase
                {
                    PayOsOrderCode = data.OrderCode,
                    Provider = "PAYOS",
                    IssueType = "UNMATCHED_PAYMENT",
                    ProviderReportedAmount = data.Amount,
                    Currency = data.Currency ?? "VND",
                    Details = $"Không tìm thấy giao dịch với order #{data.OrderCode}, reference {reference}.",
                    Status = "OPEN",
                    CreatedAt = now
                };
                _db.PaymentReconciliationCases.Add(reconCase);

                webhookEvent.RequiresManualReview = true;
                webhookEvent.ReviewReason = "Order not found in database";
                webhookEvent.Status = "REQUIRES_REVIEW";
                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return new PaymentCompletionResult
                {
                    Success = false,
                    RequiresManualReview = true,
                    ReviewReason = "Transaction not found",
                    Message = "Giao dịch không tồn tại trên hệ thống."
                };
            }

            // 3. Amount and Currency validation
            if (transaction.Amount != data.Amount || (!string.IsNullOrEmpty(data.Currency) && !string.Equals(data.Currency, "VND", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning("Amount mismatch for transaction {TransactionId}: expected {Expected} VND, received {Received} {Currency}",
                    transaction.TransactionId, transaction.Amount, data.Amount, data.Currency);

                var reconCase = new PaymentReconciliationCase
                {
                    TransactionId = transaction.TransactionId,
                    PayOsOrderCode = data.OrderCode,
                    Provider = "PAYOS",
                    IssueType = "AMOUNT_MISMATCH",
                    ExpectedAmount = transaction.Amount,
                    ProviderReportedAmount = data.Amount,
                    Currency = data.Currency ?? "VND",
                    Details = $"Số tiền không khớp: Kỳ vọng {transaction.Amount:N0} VND, nhận được {data.Amount:N0} {data.Currency}.",
                    Status = "OPEN",
                    CreatedAt = now
                };
                _db.PaymentReconciliationCases.Add(reconCase);

                transaction.RequiresManualReview = true;
                transaction.ReviewReason = $"Amount mismatch: expected {transaction.Amount}, received {data.Amount}";
                transaction.ExpectedAmount = transaction.Amount;
                transaction.ProviderReportedAmount = data.Amount;

                webhookEvent.RequiresManualReview = true;
                webhookEvent.ReviewReason = "Amount mismatch";
                webhookEvent.ExpectedAmount = transaction.Amount;
                webhookEvent.ReceivedAmount = data.Amount;
                webhookEvent.Status = "REQUIRES_REVIEW";

                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return new PaymentCompletionResult
                {
                    Success = false,
                    RequiresManualReview = true,
                    TransactionId = transaction.TransactionId,
                    UserId = transaction.UserId,
                    ReviewReason = "Amount mismatch",
                    Message = "Số tiền thanh toán không khớp với giao dịch."
                };
            }

            // 4. Status Check
            if ("SUCCESS".Equals(transaction.Status, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("Transaction {TransactionId} is already SUCCESS.", transaction.TransactionId);
                webhookEvent.Status = "PROCESSED";
                webhookEvent.ExpectedAmount = transaction.Amount;
                webhookEvent.ReceivedAmount = data.Amount;
                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return new PaymentCompletionResult
                {
                    Success = true,
                    IsDuplicate = true,
                    TransactionId = transaction.TransactionId,
                    UserId = transaction.UserId,
                    Message = "Giao dịch đã được xử lý thành công trước đó."
                };
            }

            if (!"PENDING".Equals(transaction.Status, StringComparison.OrdinalIgnoreCase) &&
                !"CREATING".Equals(transaction.Status, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Transaction {TransactionId} has terminal status {Status}.", transaction.TransactionId, transaction.Status);

                var reconCase = new PaymentReconciliationCase
                {
                    TransactionId = transaction.TransactionId,
                    PayOsOrderCode = data.OrderCode,
                    Provider = "PAYOS",
                    IssueType = "PROVIDER_STATUS_CONFLICT",
                    ExpectedAmount = transaction.Amount,
                    ProviderReportedAmount = data.Amount,
                    Currency = data.Currency ?? "VND",
                    Details = $"Thanh toán đến khi giao dịch đang ở trạng thái {transaction.Status}.",
                    Status = "OPEN",
                    CreatedAt = now
                };
                _db.PaymentReconciliationCases.Add(reconCase);

                transaction.RequiresManualReview = true;
                transaction.ReviewReason = $"Payment received while status was {transaction.Status}";

                webhookEvent.RequiresManualReview = true;
                webhookEvent.ReviewReason = $"Transaction status was {transaction.Status}";
                webhookEvent.Status = "REQUIRES_REVIEW";

                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);

                return new PaymentCompletionResult
                {
                    Success = false,
                    RequiresManualReview = true,
                    TransactionId = transaction.TransactionId,
                    UserId = transaction.UserId,
                    Message = $"Giao dịch đang ở trạng thái {transaction.Status}, cần đối soát thủ công."
                };
            }

            // 5. Direct SQL Conditional Claim (PENDING/CREATING -> SUCCESS)
            var claimed = await _db.Transactions
                .Where(t => t.TransactionId == transaction.TransactionId && (t.Status == "PENDING" || t.Status == "CREATING"))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.Status, "SUCCESS")
                    .SetProperty(t => t.CompletedAt, now)
                    .SetProperty(t => t.ReferenceCode, reference),
                    cancellationToken);

            if (claimed != 1)
            {
                // Concurrently claimed or updated
                var reloadedTx = await _db.Transactions.AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TransactionId == transaction.TransactionId, cancellationToken);

                if (reloadedTx?.Status == "SUCCESS")
                {
                    webhookEvent.Status = "PROCESSED";
                    await _db.SaveChangesAsync(cancellationToken);
                    await tx.CommitAsync(cancellationToken);
                    return new PaymentCompletionResult
                    {
                        Success = true,
                        IsDuplicate = true,
                        TransactionId = transaction.TransactionId,
                        UserId = transaction.UserId,
                        Message = "Giao dịch đã được xử lý thành công trước đó."
                    };
                }

                webhookEvent.Status = "REQUIRES_REVIEW";
                await _db.SaveChangesAsync(cancellationToken);
                await tx.CommitAsync(cancellationToken);
                return new PaymentCompletionResult
                {
                    Success = false,
                    RequiresManualReview = true,
                    Message = "Xung đột đồng thời khi cập nhật trạng thái giao dịch."
                };
            }

            // 6. User Balance & Ledger Update with Bounded Optimistic Concurrency Control (OCC) and Savepoints
            const int maxOccAttempts = 3;
            bool balanceUpdated = false;
            decimal finalNewBalance = 0;

            for (int attempt = 0; attempt < maxOccAttempts; attempt++)
            {
                var savepointName = $"occ_balance_{attempt}";
                await tx.CreateSavepointAsync(savepointName, cancellationToken);

                var userSnapshot = await _db.Users.AsNoTracking()
                    .FirstOrDefaultAsync(u => u.UserId == transaction.UserId, cancellationToken);

                if (userSnapshot == null)
                {
                    throw new InvalidOperationException($"User {transaction.UserId} not found.");
                }

                decimal prevBalance = (decimal)(userSnapshot.Balance ?? 0);
                decimal newBalance = prevBalance + data.Amount;
                int currentVersion = userSnapshot.BalanceVersion;

                // Atomic conditional update on user balance_version
                var rowsUpdated = await _db.Users
                    .Where(u => u.UserId == transaction.UserId && u.BalanceVersion == currentVersion)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(u => u.Balance, (int)newBalance)
                        .SetProperty(u => u.BalanceVersion, currentVersion + 1)
                        .SetProperty(u => u.UpdatedAt, _clock.Now),
                        cancellationToken);

                if (rowsUpdated == 1)
                {
                    try
                    {
                        await _ledgerService.AppendEntryAsync(
                            transaction.UserId,
                            transaction.TransactionId,
                            data.Amount,
                            prevBalance,
                            newBalance,
                            "DEPOSIT",
                            $"Nạp tiền tự động qua PayOS (Mã #{data.OrderCode})",
                            cancellationToken);

                        await _db.SaveChangesAsync(cancellationToken);

                        // Direct update to ensure webhookEvent is marked PROCESSED even if ChangeTracker was cleared in prior retry
                        await _db.PaymentWebhookEvents
                            .Where(e => e.WebhookEventId == webhookEvent.WebhookEventId)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(e => e.Status, "PROCESSED")
                                .SetProperty(e => e.ExpectedAmount, transaction.Amount)
                                .SetProperty(e => e.ReceivedAmount, data.Amount),
                                cancellationToken);

                        balanceUpdated = true;
                        finalNewBalance = newBalance;
                        break;
                    }
                    catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
                    {
                        // Ledger sequence conflict from concurrent deposit: rollback to savepoint & retry
                        await tx.RollbackToSavepointAsync(savepointName, cancellationToken);
                        _db.ChangeTracker.Clear();
                    }
                }
                else
                {
                    // BalanceVersion changed by concurrent writer: rollback to savepoint and retry
                    await tx.RollbackToSavepointAsync(savepointName, cancellationToken);
                }
            }

            if (!balanceUpdated)
            {
                throw new DbUpdateConcurrencyException("Không thể cập nhật số dư người dùng do xung đột đồng thời sau nhiều lần thử lại.");
            }

            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation("Successfully completed deposit for user {UserId}, amount {Amount}, new balance {Balance}",
                transaction.UserId, data.Amount, finalNewBalance);

            return new PaymentCompletionResult
            {
                Success = true,
                TransactionId = transaction.TransactionId,
                UserId = transaction.UserId,
                NewBalance = finalNewBalance,
                Message = "Nạp tiền thành công."
            };
        });
    }

    public async Task<PaymentCompletionResult> CompleteDepositDirectAsync(
        long merchantOrderCode,
        decimal receivedAmount,
        string currency,
        string providerReference,
        string provider = "PAYOS",
        CancellationToken cancellationToken = default)
    {
        var dummyPayload = new PayOsWebhookPayloadDto
        {
            Code = "00",
            Desc = "success",
            Data = new PayOsWebhookDataDto
            {
                OrderCode = merchantOrderCode,
                Amount = receivedAmount,
                Currency = currency,
                Reference = providerReference
            }
        };

        var sanitized = $"{{\"orderCode\":{merchantOrderCode},\"amount\":{receivedAmount},\"currency\":\"{currency}\",\"reference\":\"{providerReference}\"}}";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sanitized))).ToLowerInvariant();

        return await ProcessWebhookTransactionallyAsync(dummyPayload, sanitized, hash, cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("constraint failed", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("2601", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("2627", StringComparison.OrdinalIgnoreCase);
    }
}
