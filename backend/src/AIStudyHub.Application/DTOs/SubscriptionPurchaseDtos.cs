using System;

namespace AIStudyHub.Application.DTOs;

public class SubscriptionPurchaseResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public int? HistoryId { get; set; }
    public int? TransactionId { get; set; }
    public string? TierName { get; set; }
    public decimal PricePaid { get; set; }
    public DateTime? EffectiveUntil { get; set; }
}

public class SubscriptionHistoryDto
{
    public int HistoryId { get; set; }
    public int UserId { get; set; }
    public int? TransactionId { get; set; }
    public int OldTierId { get; set; }
    public int NewTierId { get; set; }
    public string? TierNameSnapshot { get; set; }
    public decimal PriceSnapshot { get; set; }
    public string CurrencySnapshot { get; set; } = "VND";
    public int DurationDaysSnapshot { get; set; }
    public int? StorageLimitSnapshot { get; set; }
    public int? AiPromptLimitSnapshot { get; set; }
    public string? PricingPolicySnapshot { get; set; }
    public string? PurchaseType { get; set; }
    public string ChangeReason { get; set; } = string.Empty;
    public DateTime ChangedAt { get; set; }
    public DateTime? PurchasedAt { get; set; }
    public DateTime? EffectiveFrom { get; set; }
    public DateTime? EffectiveUntil { get; set; }
}
