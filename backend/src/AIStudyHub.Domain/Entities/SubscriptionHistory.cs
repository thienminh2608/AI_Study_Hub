using System;

namespace AIStudyHub.Domain.Entities;

public class SubscriptionHistory
{
    public int HistoryId { get; set; }
    
    public int UserId { get; set; }
    
    public int OldTierId { get; set; }
    
    public int NewTierId { get; set; }
    
    public string ChangeReason { get; set; } = null!; // UPGRADE, AUTO_RENEW, DOWNGRADE, ADMIN_ADJUST
    
    public DateTime ChangedAt { get; set; }

    public virtual User User { get; set; } = null!;
}
