using System;

namespace AIStudyHub.Domain.Entities;

public class SubscriptionHistory
{
    public int HistoryId { get; set; }
    
    public int UserId { get; set; }
    
    public int? TransactionId { get; set; }
    
    public int OldTierId { get; set; }
    
    public int NewTierId { get; set; }
    
    public string? TierNameSnapshot { get; set; }
    
    public decimal PriceSnapshot { get; set; } = 0;
    
    public string CurrencySnapshot { get; set; } = "VND";
    
    public int DurationDaysSnapshot { get; set; } = 30;
    
    public int? StorageLimitSnapshot { get; set; }
    
    public int? AiPromptLimitSnapshot { get; set; }
    
    public string? PricingPolicySnapshot { get; set; } // INITIAL_PURCHASE_PRICE, STANDARD_CURRENT_TIER_PRICE, DOWNGRADE_POLICY, ADMIN_OVERRIDE
    
    public string? PurchaseType { get; set; } // INITIAL_PURCHASE, AUTO_RENEW, DOWNGRADE, REFUND_CANCEL, ADMIN_ADJUST
    
    public string ChangeReason { get; set; } = null!; // UPGRADE, USER_BUY, AUTO_RENEW_SUCCESS, DOWNGRADE, REFUND_CANCEL, ADMIN_ADJUST
    
    public DateTime ChangedAt { get; set; }
    
    public DateTime? PurchasedAt { get; set; }
    
    public DateTime? EffectiveFrom { get; set; }
    
    public DateTime? EffectiveUntil { get; set; }

    public virtual User User { get; set; } = null!;
    public virtual Transaction? Transaction { get; set; }
}
