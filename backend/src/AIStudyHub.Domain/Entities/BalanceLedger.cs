using System;

namespace AIStudyHub.Domain.Entities;

public class BalanceLedger
{
    public int LedgerId { get; set; }
    
    public int UserId { get; set; }
    
    public int? TransactionId { get; set; }
    
    public decimal Amount { get; set; }
    
    public decimal PreviousBalance { get; set; }
    
    public decimal CurrentBalance { get; set; }
    
    public string ActionType { get; set; } = null!; // DEPOSIT, WITHDRAW, REFUND, ADJUST
    
    public string? Description { get; set; }
    
    public DateTime CreatedAt { get; set; }
    
    public string Signature { get; set; } = null!; // SHA256 integrity signature

    public virtual User User { get; set; } = null!;
    public virtual Transaction? Transaction { get; set; }
}
