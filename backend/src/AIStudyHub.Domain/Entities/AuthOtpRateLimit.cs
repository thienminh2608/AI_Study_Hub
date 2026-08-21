using System;

namespace AIStudyHub.Domain.Entities;

public partial class AuthOtpRateLimit
{
    public string NormalizedEmailHash { get; set; } = null!;

    public string Purpose { get; set; } = null!;

    public DateTime CooldownUntil { get; set; }

    public DateTime LastSentAt { get; set; }

    public int RequestCount { get; set; }
}
