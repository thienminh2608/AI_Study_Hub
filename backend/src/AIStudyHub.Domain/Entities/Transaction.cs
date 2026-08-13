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

    public virtual User User { get; set; } = null!;
}
