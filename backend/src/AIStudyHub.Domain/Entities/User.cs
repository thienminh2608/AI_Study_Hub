using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class User
{
    public int UserId
    {
        get; set;
    }

    public string Username { get; set; } = null!;

    public string? Email
    {
        get; set;
    }

    public string? PasswordHash
    {
        get; set;
    }

    public string? Role
    {
        get; set;
    }

    public int? TierId
    {
        get; set;
    }

    public int? Balance
    {
        get; set;
    }

    public int? AiPromptsToday
    {
        get; set;
    }

    public DateTime? LastPromptReset
    {
        get; set;
    }

    public string? Status
    {
        get; set;
    }

    public DateTime? ExpiresAt
    {
        get; set;
    }

    public bool ExpiryNotified
    {
        get; set;
    }

    public bool DowngradeNoticePending
    {
        get; set;
    }

    public bool IsAutoRenew { get; set; } = true;

    public DateTime? GracePeriodEndsAt
    {
        get; set;
    }

    public int BalanceVersion { get; set; } = 0;

    public DateTime? CreatedAt
    {
        get; set;
    }

    public DateTime? UpdatedAt
    {
        get; set;
    }

    public virtual ICollection<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();

    public virtual ICollection<ChatSession> ChatSessions { get; set; } = new List<ChatSession>();

    public virtual ICollection<DocumentReport> DocumentReportReporters { get; set; } = new List<DocumentReport>();

    public virtual ICollection<DocumentReport> DocumentReportResolvedByAdmins { get; set; } = new List<DocumentReport>();

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();

    public virtual ICollection<DocumentActivity> DocumentActivities { get; set; } = new List<DocumentActivity>();

    public virtual ICollection<Folder> Folders { get; set; } = new List<Folder>();

    public virtual ICollection<Friendship> FriendshipAddressees { get; set; } = new List<Friendship>();

    public virtual ICollection<Friendship> FriendshipBlockers { get; set; } = new List<Friendship>();

    public virtual ICollection<Friendship> FriendshipRequesters { get; set; } = new List<Friendship>();

    public virtual Subscription? Tier
    {
        get; set;
    }

    public virtual ICollection<Transaction> Transactions { get; set; } = new List<Transaction>();
    public virtual ICollection<FolderShare> FolderShares { get; set; } = new List<FolderShare>();
    public virtual ICollection<DocumentVersion> DocumentVersions { get; set; } = new List<DocumentVersion>();
    public virtual ICollection<AuditLog> AuditLogs { get; set; } = new List<AuditLog>();
}
