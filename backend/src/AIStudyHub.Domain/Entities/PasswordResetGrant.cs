using System;

namespace AIStudyHub.Domain.Entities;

public partial class PasswordResetGrant
{
    public long GrantId { get; set; }

    public int UserId { get; set; }

    public Guid ChallengeId { get; set; }

    public string GrantHash { get; set; } = null!;

    public DateTime ExpiresAt { get; set; }

    public bool IsConsumed { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public byte[]? RowVersion { get; set; }

    public virtual User User { get; set; } = null!;
}
