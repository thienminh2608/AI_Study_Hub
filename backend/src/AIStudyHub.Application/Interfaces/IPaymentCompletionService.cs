using System;
using System.Threading;
using System.Threading.Tasks;

namespace AIStudyHub.Application.Interfaces;

public class PaymentCompletionResult
{
    public bool Success { get; set; }
    public bool IsDuplicate { get; set; }
    public bool RequiresManualReview { get; set; }
    public string? ReviewReason { get; set; }
    public string? Message { get; set; }
    public int? TransactionId { get; set; }
    public int? UserId { get; set; }
    public decimal? NewBalance { get; set; }
}

public interface IPaymentCompletionService
{
    Task<PaymentCompletionResult> ProcessWebhookTransactionallyAsync(
        PayOsWebhookPayloadDto payload,
        string sanitizedPayloadJson,
        string payloadHash,
        CancellationToken cancellationToken = default);

    Task<PaymentCompletionResult> CompleteDepositDirectAsync(
        long merchantOrderCode,
        decimal receivedAmount,
        string currency,
        string providerReference,
        string provider = "PAYOS",
        CancellationToken cancellationToken = default);
}
