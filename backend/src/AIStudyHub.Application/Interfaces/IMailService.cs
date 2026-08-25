namespace AIStudyHub.Application.Interfaces;

public interface IMailService
{
    bool SendOtp(string toEmail, string otp);
    bool SendPremiumExpiryWarning(string toEmail, string username, int hoursLeft);
    bool SendPremiumDowngraded(string toEmail, string username);
    Task<bool> SendDocumentSharedNotificationAsync(
        string toEmail,
        string recipientName,
        string sharedByName,
        string documentTitle,
        string role,
        DateTime sharedAt,
        string documentUrl);
}
