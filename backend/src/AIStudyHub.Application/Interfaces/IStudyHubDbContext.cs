using Microsoft.EntityFrameworkCore;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace AIStudyHub.Application.Interfaces;

public interface IStudyHubDbContext
{
    DbSet<Bookmark> Bookmarks { get; set; }
    DbSet<ChatMessage> ChatMessages { get; set; }
    DbSet<ChatSession> ChatSessions { get; set; }
    DbSet<Document> Documents { get; set; }
    DbSet<DocumentExtractedText> DocumentExtractedTexts { get; set; }
    DbSet<DocumentReport> DocumentReports { get; set; }
    DbSet<Folder> Folders { get; set; }
    DbSet<Friendship> Friendships { get; set; }
    DbSet<ReportReasonConfig> ReportReasonConfigs { get; set; }
    DbSet<Subscription> Subscriptions { get; set; }
    DbSet<Transaction> Transactions { get; set; }
    DbSet<User> Users { get; set; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    DatabaseFacade Database { get; }
}
