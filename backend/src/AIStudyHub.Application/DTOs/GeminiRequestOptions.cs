namespace AIStudyHub.Application.DTOs;

public class GeminiRequestOptions
{
    public int MaxOutputTokens { get; set; } = 1024;
    public int TimeoutSeconds { get; set; } = 60;
    public int MaxRetries { get; set; } = 2;
    public int RetryBaseDelayMilliseconds { get; set; } = 500;
}