using System;

namespace AIStudyHub.Application.DTOs;

public class GeminiResponseDto
{
    public string Content { get; set; } = string.Empty;
    public string Provider { get; set; } = "Google";
    public string Model { get; set; } = string.Empty;
    public string Operation { get; set; } = "CHAT";
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int CachedTokens { get; set; }
    public int TotalTokens { get; set; }
    public long LatencyMs { get; set; }
    public string Status { get; set; } = "SUCCESS";
    public string? ErrorCode { get; set; }
    public decimal EstimatedCost { get; set; }
    public string Currency { get; set; } = "USD";
    public string PricingVersion { get; set; } = "2026.1";
    public string RequestId { get; set; } = Guid.NewGuid().ToString("N");
}
