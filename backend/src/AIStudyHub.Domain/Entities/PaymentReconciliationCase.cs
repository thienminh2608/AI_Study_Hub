using System;

namespace AIStudyHub.Domain.Entities;

public partial class PaymentReconciliationCase
{
    public long CaseId { get; set; }

    public int? TransactionId { get; set; }

    public long? PayOsOrderCode { get; set; }

    public string Provider { get; set; } = "PAYOS";

    public string IssueType { get; set; } = "AMOUNT_MISMATCH"; // AMOUNT_MISMATCH, UNMATCHED_PAYMENT, PROVIDER_STATUS_CONFLICT, RECONCILIATION_ERROR

    public decimal? ExpectedAmount { get; set; }

    public decimal? ProviderReportedAmount { get; set; }

    public string Currency { get; set; } = "VND";

    public string Details { get; set; } = string.Empty;

    public string Status { get; set; } = "OPEN"; // OPEN, RESOLVED, IGNORED

    public DateTime? ResolvedAt { get; set; }

    public int? ResolvedByUserId { get; set; }

    public string? ResolutionNotes { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual Transaction? Transaction { get; set; }

    public virtual User? ResolvedByUser { get; set; }
}
