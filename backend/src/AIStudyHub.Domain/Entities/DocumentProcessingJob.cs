using System;

namespace AIStudyHub.Domain.Entities;

public partial class DocumentProcessingJob
{
    public int JobId { get; set; }
    public int DocumentId { get; set; }
    public int? DocumentVersionId { get; set; }
    public string Status { get; set; } = "QUEUED"; // QUEUED, PROCESSING, COMPLETED, FAILED, DEAD
    public int AttemptCount { get; set; } = 0;
    public int MaxAttempts { get; set; } = 3;
    public DateTime AvailableAt { get; set; } = DateTime.UtcNow;
    public DateTime? LockedAt { get; set; }
    public DateTime? LockedUntil { get; set; }
    public string? LockedBy { get; set; }
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    public virtual Document Document { get; set; } = null!;
    public virtual DocumentVersion? DocumentVersion { get; set; }
}
