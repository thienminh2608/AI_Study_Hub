using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class Transaction
{
    public int TransactionId
    {
        get; set;
    }

    public int UserId
    {
        get; set;
    }

    public decimal Amount
    {
        get; set;
    }

    public string Type { get; set; } = null!;

    public string? Status
    {
        get; set;
    }

    public DateTime? StartedAt
    {
        get; set;
    }

    public DateTime? CompletedAt
    {
        get; set;
    }

    public string? ReferenceCode { get; set; }
    public string? BankId { get; set; }
    public int? ApproverId { get; set; }
    public string? FailureReason { get; set; }

    public virtual User User { get; set; } = null!;
    public virtual User? Approver { get; set; }
}
