using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AIStudyHub.Application.Interfaces;

public interface IStudyHubDbContext
{
    DbSet<Bookmark> Bookmarks
    {
        get; set;
    }
    DbSet<ChatMessage> ChatMessages
    {
        get; set;
    }
    DbSet<ChatSession> ChatSessions
    {
        get; set;
    }
    DbSet<Document> Documents
    {
        get; set;
    }
    DbSet<DocumentActivity> DocumentActivities
    {
        get; set;
    }
    DbSet<DocumentShare> DocumentShares { get; set; }
    DbSet<DocumentExtractedText> DocumentExtractedTexts
    {
        get; set;
    }
    DbSet<DocumentOcrRegion> DocumentOcrRegions
    {
        get; set;
    }
    DbSet<DocumentChunk> DocumentChunks
    {
        get; set;
    }
    DbSet<DocumentReport> DocumentReports
    {
        get; set;
    }
    DbSet<ModerationAction> ModerationActions
    {
        get; set;
    }
    DbSet<ModerationAppeal> ModerationAppeals
    {
        get; set;
    }
    DbSet<ModerationNotice> ModerationNotices
    {
        get; set;
    }
    DbSet<Folder> Folders
    {
        get; set;
    }
    DbSet<Friendship> Friendships
    {
        get; set;
    }
    DbSet<ReportReasonConfig> ReportReasonConfigs
    {
        get; set;
    }
    DbSet<Subscription> Subscriptions
    {
        get; set;
    }
    DbSet<TransferConfiguration> TransferConfigurations
    {
        get; set;
    }
    DbSet<Transaction> Transactions
    {
        get; set;
    }
    DbSet<User> Users
    {
        get; set;
    }
    DbSet<FolderShare> FolderShares { get; set; }
    DbSet<DocumentVersion> DocumentVersions { get; set; }
    DbSet<AuditLog> AuditLogs { get; set; }
    DbSet<BalanceLedger> BalanceLedgers { get; set; }
    DbSet<SubscriptionHistory> SubscriptionHistories { get; set; }
    DbSet<DocumentProcessingJob> DocumentProcessingJobs { get; set; }
    DbSet<AiUsage> AiUsages { get; set; }
    DbSet<SubjectCategory> SubjectCategories { get; set; }
    DbSet<ChatMessageCitation> ChatMessageCitations { get; set; }
    DbSet<RefreshTokenSession> RefreshTokenSessions { get; set; }
    DbSet<AuthOtpChallenge> AuthOtpChallenges { get; set; }
    DbSet<PasswordResetGrant> PasswordResetGrants { get; set; }
    DbSet<PaymentWebhookEvent> PaymentWebhookEvents { get; set; }
    DbSet<PaymentReconciliationCase> PaymentReconciliationCases { get; set; }
    DbSet<AuthOtpRateLimit> AuthOtpRateLimits { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    DatabaseFacade Database
    {
        get;
    }
    Microsoft.EntityFrameworkCore.ChangeTracking.ChangeTracker ChangeTracker
    {
        get;
    }
}
