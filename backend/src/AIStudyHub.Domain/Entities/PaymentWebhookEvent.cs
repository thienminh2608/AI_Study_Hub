using System;

namespace AIStudyHub.Domain.Entities;

public partial class PaymentWebhookEvent
{
    public long WebhookEventId { get; set; }

    public string Provider { get; set; } = "PAYOS";

    public string ProviderEventId { get; set; } = null!;

    public long? MerchantOrderCode { get; set; }

    public string? PayloadHash { get; set; }

    public string? PayloadSanitized { get; set; }

    public decimal? ExpectedAmount { get; set; }

    public decimal? ReceivedAmount { get; set; }

    public string? Currency { get; set; }

    public bool RequiresManualReview { get; set; }

    public string? ReviewReason { get; set; }

    public bool IsSyntheticReference { get; set; }

    public DateTime ProcessedAt { get; set; }

    public string Status { get; set; } = "RECEIVED"; // RECEIVED, PROCESSED, REQUIRES_REVIEW, DUPLICATE, INVALID_SIGNATURE, FAILED

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; }

    public byte[]? RowVersion { get; set; }
}
