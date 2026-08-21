using System;

namespace AIStudyHub.Domain.Entities;

public class BalanceLedger
{
    public long LedgerId { get; set; }
    
    public int UserId { get; set; }
    
    public long LedgerSequence { get; set; } // Sequence per user: 1, 2, 3...
    
    public int? TransactionId { get; set; }
    
    public decimal Amount { get; set; }
    
    public decimal PreviousBalance { get; set; }
    
    public decimal CurrentBalance { get; set; }
    
    public string ActionType { get; set; } = null!; // DEPOSIT, PURCHASE, REFUND_PURCHASE, REVERSE_DEPOSIT, OPENING_BALANCE
    
    public string? Description { get; set; }
    
    public string PreviousHash { get; set; } = null!; // "GENESIS" for sequence 1
    
    public string CurrentHash { get; set; } = null!; // HMAC-SHA256 signature
    
    public int HashVersion { get; set; } = 1;
    
    public int KeyVersion { get; set; } = 1;
    
    public DateTime CreatedAtUtc { get; set; }

    public virtual User User { get; set; } = null!;
    public virtual Transaction? Transaction { get; set; }
}
