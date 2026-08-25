using System;
using AIStudyHub.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Infrastructure.Services.Email;

public class MockMailService : IMailService
{
    private readonly ILogger<MockMailService> _logger;

    public MockMailService(ILogger<MockMailService> logger)
    {
        _logger = logger;
    }

    public bool SendOtp(string toEmail, string otp)
    {
        _logger.LogWarning("[MOCK EMAIL] OTP Sent to {Email}. Code: {Otp}", toEmail, otp);
        Console.WriteLine($"\n[MOCK EMAIL] OTP Sent to {toEmail}. Code: {otp}\n");
        return true;
    }

    public bool SendPremiumExpiryWarning(string toEmail, string username, int hoursLeft)
    {
        _logger.LogWarning("[MOCK EMAIL] Premium Expiry Warning sent to {Email} ({Username}). Expiry in {Hours} hours.", toEmail, username, hoursLeft);
        return true;
    }

    public bool SendPremiumDowngraded(string toEmail, string username)
    {
        _logger.LogWarning("[MOCK EMAIL] Premium Downgraded notification sent to {Email} ({Username}).", toEmail, username);
        return true;
    }

    public Task<bool> SendDocumentSharedNotificationAsync(
        string toEmail,
        string recipientName,
        string sharedByName,
        string documentTitle,
        string role,
        DateTime sharedAt,
        string documentUrl)
    {
        _logger.LogWarning(
            "[MOCK EMAIL] Document share notification sent to {Email}. Document: {DocumentTitle}, Role: {Role}, SharedAt: {SharedAt}, Url: {DocumentUrl}",
            toEmail,
            documentTitle,
            role,
            sharedAt,
            documentUrl);
        return Task.FromResult(true);
    }
}
