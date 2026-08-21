using System;

namespace AIStudyHub.Domain.Entities;

public partial class RefreshTokenSession
{
    public long SessionId { get; set; }

    public int UserId { get; set; }

    public Guid TokenFamilyId { get; set; }

    public long? ParentSessionId { get; set; }

    public string TokenHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public string? CreatedByIp { get; set; }

    public string? UserAgent { get; set; }

    public DateTime? RevokedAt { get; set; }

    public string? RevokedReason { get; set; }

    public string? RevokedByIp { get; set; }

    public string? ReplacedByTokenHash { get; set; }

    public bool IsUsed { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public byte[]? RowVersion { get; set; }

    public virtual User User { get; set; } = null!;
}
