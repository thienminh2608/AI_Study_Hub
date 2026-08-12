using System;

namespace AIStudyHub.Application.DTOs;

public class TransactionDto
{
    public int TransactionId { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = null!;
    public decimal Amount { get; set; }
    public string Type { get; set; } = null!; // DEPOSIT or WITHDRAW
    public string Status { get; set; } = null!; // PENDING, SUCCESS, etc.
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class CreateTransactionDto
{
    public decimal Amount { get; set; }
    public string Type { get; set; } = null!; // DEPOSIT or WITHDRAW
}

public class UpdateTransactionStatusDto
{
    public string Status { get; set; } = null!; // SUCCESS, CANCELLED
}

public class SubscriptionDto
{
    public int TierId { get; set; }
    public string TierName { get; set; } = null!;
    public int MaxStorageMb { get; set; }
    public int AiPromptLimitPerDay { get; set; }
    public decimal Price { get; set; }
    public int TotalStorageMb { get; set; }
}
