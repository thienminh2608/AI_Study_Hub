using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AIStudyHub.UnitTests;

public class DocumentShareEmailNotificationTests : IDisposable
{
    private readonly TestDbContextFactory _factory = new();

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task New_Document_Share_Sends_One_Email_With_Current_Share_Data()
    {
        using var db = _factory.CreateContext();
        SeedShareData(db, 101, "Giải tích & Đại số");
        var mail = new RecordingMailService();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://aistudyhub.com"
            })
            .Build();
        var service = new PermissionService(db, mail, configuration);

        await service.AddOrUpdateDocumentUserShareAsync(101, "recipient@test.com", "VIEWER", 1);
        await service.AddOrUpdateDocumentUserShareAsync(101, "recipient@test.com", "EDITOR", 1);

        var sent = Assert.Single(mail.DocumentShareNotifications);
        Assert.Equal("recipient@test.com", sent.ToEmail);
        Assert.Equal("recipientUser", sent.RecipientName);
        Assert.Equal("ownerUser", sent.SharedByName);
        Assert.Equal("Giải tích & Đại số", sent.DocumentTitle);
        Assert.Equal("VIEWER", sent.Role);
        Assert.Equal("https://aistudyhub.com/document/101", sent.DocumentUrl);
        Assert.NotEqual(default, sent.SharedAt);
    }

    [Fact]
    public async Task Failed_Email_Does_Not_Roll_Back_Document_Share()
    {
        using var db = _factory.CreateContext();
        SeedShareData(db, 102, "Tài liệu kiểm thử");
        var mail = new RecordingMailService { DocumentShareResult = false };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Frontend:BaseUrl"] = "https://aistudyhub.com"
            })
            .Build();
        var service = new PermissionService(db, mail, configuration);

        await service.AddOrUpdateDocumentUserShareAsync(102, "recipient@test.com", "VIEWER", 1);

        Assert.True(await db.DocumentShares.AnyAsync(s =>
            s.DocumentId == 102 && s.SharedWithUserId == 2 && s.Role == "VIEWER"));
        Assert.Single(mail.DocumentShareNotifications);
    }

    private static void SeedShareData(
        AIStudyHub.Infrastructure.Persistence.StudyHubDbContext db,
        int documentId,
        string title)
    {
        db.Subscriptions.Add(new Subscription
        {
            TierId = 1,
            TierName = "Free",
            Price = 0,
            MaxStorageMb = 50,
            TotalStorageMb = 50,
            AiPromptLimitPerDay = 5
        });
        db.Users.AddRange(
            new User
            {
                UserId = 1,
                Username = "ownerUser",
                Email = "owner@test.com",
                PasswordHash = "hash",
                Role = "STUDENT",
                Status = "ACTIVE",
                TierId = 1
            },
            new User
            {
                UserId = 2,
                Username = "recipientUser",
                Email = "recipient@test.com",
                PasswordHash = "hash",
                Role = "STUDENT",
                Status = "ACTIVE",
                TierId = 1
            });
        db.Documents.Add(new Document
        {
            DocumentId = documentId,
            UserId = 1,
            Title = title,
            FileExtension = "pdf",
            CloudStorageUrl = $"/uploads/1/{documentId}.pdf",
            SharingPermission = "PRIVATE"
        });
        db.SaveChanges();
    }

    private sealed class RecordingMailService : IMailService
    {
        public bool DocumentShareResult { get; init; } = true;
        public List<DocumentShareNotification> DocumentShareNotifications { get; } = [];

        public bool SendOtp(string toEmail, string otp) => true;
        public bool SendPremiumExpiryWarning(string toEmail, string username, int hoursLeft) => true;
        public bool SendPremiumDowngraded(string toEmail, string username) => true;

        public Task<bool> SendDocumentSharedNotificationAsync(
            string toEmail,
            string recipientName,
            string sharedByName,
            string documentTitle,
            string role,
            DateTime sharedAt,
            string documentUrl)
        {
            DocumentShareNotifications.Add(new DocumentShareNotification(
                toEmail,
                recipientName,
                sharedByName,
                documentTitle,
                role,
                sharedAt,
                documentUrl));
            return Task.FromResult(DocumentShareResult);
        }
    }

    private sealed record DocumentShareNotification(
        string ToEmail,
        string RecipientName,
        string SharedByName,
        string DocumentTitle,
        string Role,
        DateTime SharedAt,
        string DocumentUrl);
}
