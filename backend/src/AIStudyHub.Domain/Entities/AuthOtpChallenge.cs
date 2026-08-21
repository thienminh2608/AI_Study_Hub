using System;

namespace AIStudyHub.Domain.Entities;

public partial class AuthOtpChallenge
{
    public Guid ChallengeId { get; set; }

    public string NormalizedEmailHash { get; set; } = null!;

    public string Purpose { get; set; } = "PASSWORD_RESET";

    public string OtpHash { get; set; } = null!;

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = 5;

    public DateTime CooldownUntil { get; set; }

    public DateTime ExpiresAt { get; set; }

    public DateTime? ConsumedAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public byte[]? RowVersion { get; set; }
}
