using System;
using System.Collections.Generic;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Infrastructure.Persistence;

public partial class StudyHubDbContext : DbContext, IStudyHubDbContext
{
    public StudyHubDbContext()
    {
    }

    public StudyHubDbContext(DbContextOptions<StudyHubDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Bookmark> Bookmarks
    {
        get; set;
    }

    public virtual DbSet<ChatMessage> ChatMessages
    {
        get; set;
    }

    public virtual DbSet<ChatSession> ChatSessions
    {
        get; set;
    }

    public virtual DbSet<Document> Documents
    {
        get; set;
    }

    public virtual DbSet<DocumentActivity> DocumentActivities
    {
        get; set;
    }
    public virtual DbSet<DocumentShare> DocumentShares { get; set; }

    public virtual DbSet<DocumentExtractedText> DocumentExtractedTexts
    {
        get; set;
    }

    public virtual DbSet<DocumentChunk> DocumentChunks
    {
        get; set;
    }

    public virtual DbSet<DocumentReport> DocumentReports
    {
        get; set;
    }
    public virtual DbSet<ModerationAction> ModerationActions
    {
        get; set;
    }
    public virtual DbSet<ModerationAppeal> ModerationAppeals
    {
        get; set;
    }
    public virtual DbSet<ModerationNotice> ModerationNotices
    {
        get; set;
    }

    public virtual DbSet<Folder> Folders
    {
        get; set;
    }

    public virtual DbSet<Friendship> Friendships
    {
        get; set;
    }

    public virtual DbSet<ReportReasonConfig> ReportReasonConfigs
    {
        get; set;
    }

    public virtual DbSet<Subscription> Subscriptions
    {
        get; set;
    }
    public virtual DbSet<TransferConfiguration> TransferConfigurations
    {
        get; set;
    }

    public virtual DbSet<Transaction> Transactions
    {
        get; set;
    }

    public virtual DbSet<User> Users
    {
        get; set;
    }
    public virtual DbSet<FolderShare> FolderShares { get; set; }
    public virtual DbSet<DocumentVersion> DocumentVersions { get; set; }
    public virtual DbSet<AuditLog> AuditLogs { get; set; }
    public virtual DbSet<BalanceLedger> BalanceLedgers { get; set; }
    public virtual DbSet<SubscriptionHistory> SubscriptionHistories { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Bookmark>(entity =>
        {
            entity.HasKey(e => e.BookmarkId).HasName("PK__bookmark__D9C65802B76AD4EB");

            entity.ToTable("bookmarks");

            entity.HasIndex(e => new { e.UserId, e.DocumentId }, "UQ_user_document").IsUnique();

            entity.Property(e => e.BookmarkId).HasColumnName("bookmark_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("created_at");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Document).WithMany(p => p.Bookmarks)
                .HasForeignKey(d => d.DocumentId)
                .HasConstraintName("FK__bookmarks__docum__7A672E12");

            entity.HasOne(d => d.User).WithMany(p => p.Bookmarks)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__bookmarks__user___797309D9");
        });

        modelBuilder.Entity<ChatMessage>(entity =>
        {
            entity.HasKey(e => e.MessageId).HasName("PK__chat_mes__0BBF6EE6E95120F9");

            entity.ToTable("chat_messages");

            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("created_at");
            entity.Property(e => e.Display)
                .HasDefaultValue(true)
                .HasColumnName("display");
            entity.Property(e => e.MessageContent).HasColumnName("message_content");
            entity.Property(e => e.Sender)
                .HasMaxLength(10)
                .HasColumnName("sender");
            entity.Property(e => e.SessionId).HasColumnName("session_id");

            entity.HasOne(d => d.Session).WithMany(p => p.ChatMessages)
                .HasForeignKey(d => d.SessionId)
                .HasConstraintName("FK__chat_mess__sessi__04E4BC85");
        });

        modelBuilder.Entity<ChatSession>(entity =>
        {
            entity.HasKey(e => e.SessionId).HasName("PK__chat_ses__69B13FDC4864A485");

            entity.ToTable("chat_sessions");

            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("created_at");
            entity.Property(e => e.IsPinned)
                .HasDefaultValue(false)
                .HasColumnName("is_pinned");
            entity.Property(e => e.AttachedDocumentId).HasColumnName("attached_document_id");
            entity.Property(e => e.SessionName)
                .HasMaxLength(255)
                .HasColumnName("session_name");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.ChatSessions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__chat_sess__user___7F2BE32F");
        });

        modelBuilder.Entity<Document>(entity =>
        {
            entity.HasKey(e => e.DocumentId).HasName("PK__document__9666E8AC7D9D1A38");

            entity.ToTable("documents", tb => tb.HasTrigger("trg_documents_updated_at"));

            entity.HasIndex(e => e.ShareLinkToken, "UQ__document__80F6B1287778587F").IsUnique();

            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.AiParsingStatus)
                .HasMaxLength(20)
                .HasDefaultValue("PENDING")
                .HasColumnName("ai_parsing_status");
            entity.Property(e => e.BookmarkCount)
                .HasDefaultValue(0)
                .HasColumnName("bookmark_count");
            entity.Property(e => e.CloudStorageUrl)
                .HasMaxLength(500)
                .HasColumnName("cloud_storage_url");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("created_at");
            entity.Property(e => e.DownloadCount)
                .HasDefaultValue(0)
                .HasColumnName("download_count");
            entity.Property(e => e.ViewCount)
                .HasDefaultValue(0)
                .HasColumnName("view_count");
            entity.Property(e => e.ExtractionCoveragePercent).HasColumnName("extraction_coverage_percent");
            entity.Property(e => e.FileExtension)
                .HasMaxLength(10)
                .HasColumnName("file_extension");
            entity.Property(e => e.FileSizeMb)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("file_size_mb");
            entity.Property(e => e.FolderId)
                .HasDefaultValueSql("(NULL)")
                .HasColumnName("folder_id");
            entity.Property(e => e.IsFlagged)
                .HasDefaultValue(false)
                .HasColumnName("is_flagged");
            entity.Property(e => e.ShareLinkToken)
                .HasMaxLength(100)
                .HasColumnName("share_link_token");
            entity.Property(e => e.GeneralAccess).HasMaxLength(20).HasDefaultValue("RESTRICTED").HasColumnName("general_access");
            entity.Property(e => e.LifeCycleStatus).HasMaxLength(30).HasDefaultValue("PRIVATE").HasColumnName("lifecycle_status");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false).HasColumnName("is_deleted");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.DeletedByUserId).HasColumnName("deleted_by_user_id");
            entity.Property(e => e.CurrentVersionId).HasColumnName("current_version_id");
            entity.Property(e => e.ShareLinkExpiresAt).HasColumnName("share_link_expires_at");
            entity.Property(e => e.IsShareLinkRevoked).HasDefaultValue(false).HasColumnName("is_share_link_revoked");
            entity.Property(e => e.SharingPermission)
                .HasMaxLength(20)
                .HasDefaultValue("PRIVATE")
                .HasColumnName("sharing_permission");
            entity.Property(e => e.RequestedVisibility).HasMaxLength(20).HasDefaultValue("PRIVATE").HasColumnName("requested_visibility");
            entity.Property(e => e.ModerationStatus).HasMaxLength(30).HasDefaultValue("NOT_REQUESTED").HasColumnName("moderation_status");
            entity.Property(e => e.ModerationSubmittedAt).HasColumnName("moderation_submitted_at");
            entity.Property(e => e.ModeratedAt).HasColumnName("moderated_at");
            entity.Property(e => e.ModeratedByUserId).HasColumnName("moderated_by_user_id");
            entity.Property(e => e.ModerationNote).HasMaxLength(1000).HasColumnName("moderation_note");
            entity.Property(e => e.Title)
                .HasMaxLength(255)
                .HasColumnName("title");
            entity.Property(e => e.Subject)
                .HasMaxLength(100)
                .HasDefaultValue("Khác")
                .HasColumnName("subject");
            entity.Property(e => e.TotalReportScore)
                .HasDefaultValue(0.0m)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("total_report_score");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.Folder).WithMany(p => p.Documents)
                .HasForeignKey(d => d.FolderId)
                .HasConstraintName("FK__documents__folde__73BA3083");

            entity.HasOne(d => d.User).WithMany(p => p.Documents)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__documents__user___72C60C4A");
        });

        modelBuilder.Entity<DocumentActivity>(entity =>
        {
            entity.HasKey(e => e.ActivityId);
            entity.ToTable("document_activities");
            entity.HasIndex(e => new { e.DocumentId, e.ActivityType });
            entity.Property(e => e.ActivityId).HasColumnName("activity_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ActivityType).HasMaxLength(20).HasColumnName("activity_type");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
            entity.HasOne(e => e.Document).WithMany(d => d.DocumentActivities).HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.User).WithMany(u => u.DocumentActivities).HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DocumentShare>(entity =>
        {
            entity.HasKey(e => e.ShareId);
            entity.ToTable("document_shares");
            entity.HasIndex(e => new { e.DocumentId, e.SharedWithUserId }).IsUnique();
            entity.Property(e => e.ShareId).HasColumnName("share_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(e => e.SharedWithUserId).HasColumnName("shared_with_user_id");
            entity.Property(e => e.Role).HasMaxLength(20).HasDefaultValue("VIEWER").HasColumnName("role");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
            entity.HasOne(e => e.Document).WithMany(p => p.DocumentShares).HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.SharedWithUser).WithMany().HasForeignKey(e => e.SharedWithUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<FolderShare>(entity =>
        {
            entity.HasKey(e => e.ShareId);
            entity.ToTable("folder_shares");
            entity.HasIndex(e => new { e.FolderId, e.SharedWithUserId }).IsUnique();
            entity.Property(e => e.ShareId).HasColumnName("share_id");
            entity.Property(e => e.FolderId).HasColumnName("folder_id");
            entity.Property(e => e.OwnerUserId).HasColumnName("owner_user_id");
            entity.Property(e => e.SharedWithUserId).HasColumnName("shared_with_user_id");
            entity.Property(e => e.Role).HasMaxLength(20).HasDefaultValue("VIEWER").HasColumnName("role");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
            entity.HasOne(e => e.Folder).WithMany(p => p.FolderShares).HasForeignKey(e => e.FolderId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.SharedWithUser).WithMany(p => p.FolderShares).HasForeignKey(e => e.SharedWithUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DocumentVersion>(entity =>
        {
            entity.HasKey(e => e.VersionId);
            entity.ToTable("document_versions");
            entity.HasIndex(e => new { e.DocumentId, e.VersionNumber }).IsUnique();
            entity.Property(e => e.VersionId).HasColumnName("version_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.VersionNumber).HasColumnName("version_number");
            entity.Property(e => e.CloudStorageUrl).HasMaxLength(500).HasColumnName("cloud_storage_url");
            entity.Property(e => e.FileExtension).HasMaxLength(10).HasColumnName("file_extension");
            entity.Property(e => e.FileSizeMb).HasColumnType("decimal(5, 2)").HasColumnName("file_size_mb");
            entity.Property(e => e.ChangeSummary).HasMaxLength(500).HasColumnName("change_summary");
            entity.Property(e => e.CreatedByUserId).HasColumnName("created_by_user_id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
            entity.HasOne(e => e.Document).WithMany(p => p.DocumentVersions).HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.CreatedByUser).WithMany(p => p.DocumentVersions).HasForeignKey(e => e.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.AuditId);
            entity.ToTable("audit_logs");
            entity.Property(e => e.AuditId).HasColumnName("audit_id");
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(e => e.Action).HasMaxLength(50).HasColumnName("action");
            entity.Property(e => e.TargetType).HasMaxLength(20).HasColumnName("target_type");
            entity.Property(e => e.TargetId).HasColumnName("target_id");
            entity.Property(e => e.Details).HasMaxLength(2000).HasColumnName("details");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
            entity.HasOne(e => e.ActorUser).WithMany(p => p.AuditLogs).HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DocumentExtractedText>(entity =>
        {
            entity.HasKey(e => e.ExtractionId).HasName("PK__document__BCC16E1649B63322");

            entity.ToTable("document_extracted_text");

            entity.HasIndex(e => e.DocumentId, "UQ__document__9666E8AD5264E5FA").IsUnique();

            entity.Property(e => e.ExtractionId).HasColumnName("extraction_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("created_at");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.ExtractedText).HasColumnName("extracted_text");

            entity.HasOne(d => d.Document).WithOne(p => p.DocumentExtractedText)
                .HasForeignKey<DocumentExtractedText>(d => d.DocumentId)
                .HasConstraintName("FK__document___docum__10566F31");
        });

        modelBuilder.Entity<DocumentReport>(entity =>
        {
            entity.HasKey(e => e.ReportId).HasName("PK__document__779B7C58B6B4C46C");

            entity.ToTable("document_reports");

            entity.HasIndex(e => new { e.DocumentId, e.ReporterId }, "UQ_document_reporter").IsUnique();

            entity.Property(e => e.ReportId).HasColumnName("report_id");
            entity.Property(e => e.AdditionalDetails)
                .HasMaxLength(500)
                .HasColumnName("additional_details");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("created_at");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.ReasonCode)
                .HasMaxLength(50)
                .HasColumnName("reason_code");
            entity.Property(e => e.ReportType).HasMaxLength(20).HasDefaultValue("COMMUNITY").HasColumnName("report_type");
            entity.Property(e => e.ClaimantName).HasMaxLength(150).HasColumnName("claimant_name");
            entity.Property(e => e.ClaimantEmail).HasMaxLength(200).HasColumnName("claimant_email");
            entity.Property(e => e.OriginalWorkUrl).HasMaxLength(1000).HasColumnName("original_work_url");
            entity.Property(e => e.EvidenceDescription).HasMaxLength(2000).HasColumnName("evidence_description");
            entity.Property(e => e.AssignedModeratorId).HasColumnName("assigned_moderator_id");
            entity.Property(e => e.ModeratorNote).HasMaxLength(1000).HasColumnName("moderator_note");
            entity.Property(e => e.PreviousSharingPermission).HasMaxLength(20).HasColumnName("previous_sharing_permission");
            entity.Property(e => e.RestrictedAt).HasColumnName("restricted_at");
            entity.Property(e => e.ReporterId).HasColumnName("reporter_id");
            entity.Property(e => e.ResolvedAt).HasColumnName("resolved_at");
            entity.Property(e => e.ResolvedByAdminId).HasColumnName("resolved_by_admin_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("PENDING")
                .HasColumnName("status");

            entity.HasOne(d => d.Document).WithMany(p => p.DocumentReports)
                .HasForeignKey(d => d.DocumentId)
                .HasConstraintName("FK__document___docum__245D67DE");

            entity.HasOne(d => d.ReasonCodeNavigation).WithMany(p => p.DocumentReports)
                .HasForeignKey(d => d.ReasonCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__document___reaso__2645B050");

            entity.HasOne(d => d.Reporter).WithMany(p => p.DocumentReportReporters)
                .HasForeignKey(d => d.ReporterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__document___repor__25518C17");

            entity.HasOne(d => d.ResolvedByAdmin).WithMany(p => p.DocumentReportResolvedByAdmins)
                .HasForeignKey(d => d.ResolvedByAdminId)
                .HasConstraintName("FK__document___resol__2739D489");
        });

        modelBuilder.Entity<Folder>(entity =>
        {
            entity.HasKey(e => e.FolderId).HasName("PK__folders__0045071B08AC39F5");

            entity.ToTable("folders");

            entity.Property(e => e.FolderId).HasColumnName("folder_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("created_at");
            entity.Property(e => e.FolderName)
                .HasMaxLength(100)
                .HasColumnName("folder_name");
            entity.Property(e => e.ParentFolderId)
                .HasDefaultValueSql("(NULL)")
                .HasColumnName("parent_folder_id");
            entity.Property(e => e.GeneralAccess).HasMaxLength(20).HasDefaultValue("RESTRICTED").HasColumnName("general_access");
            entity.Property(e => e.IsDeleted).HasDefaultValue(false).HasColumnName("is_deleted");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.SharingPermission)
                .HasMaxLength(20)
                .HasDefaultValue("PRIVATE")
                .HasColumnName("sharing_permission");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.ParentFolder).WithMany(p => p.InverseParentFolder)
                .HasForeignKey(d => d.ParentFolderId)
                .HasConstraintName("FK__folders__parent___6477ECF3");

            entity.HasOne(d => d.User).WithMany(p => p.Folders)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK__folders__user_id__6383C8BA");
        });

        modelBuilder.Entity<Friendship>(entity =>
        {
            entity.HasKey(e => e.FriendshipId).HasName("PK__friendsh__BC802BCF248A674B");

            entity.ToTable("friendships", tb => tb.HasTrigger("trg_friendships_updated_at"));

            entity.HasIndex(e => new { e.RequesterId, e.AddresseeId }, "UQ_friendship").IsUnique();

            entity.Property(e => e.FriendshipId).HasColumnName("friendship_id");
            entity.Property(e => e.AddresseeId).HasColumnName("addressee_id");
            entity.Property(e => e.BlockerId).HasColumnName("blocker_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("created_at");
            entity.Property(e => e.RequesterId).HasColumnName("requester_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("PENDING")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("updated_at");

            entity.HasOne(d => d.Addressee).WithMany(p => p.FriendshipAddressees)
                .HasForeignKey(d => d.AddresseeId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__friendshi__addre__18EBB532");

            entity.HasOne(d => d.Blocker).WithMany(p => p.FriendshipBlockers)
                .HasForeignKey(d => d.BlockerId)
                .HasConstraintName("FK_friendships_blocker");

            entity.HasOne(d => d.Requester).WithMany(p => p.FriendshipRequesters)
                .HasForeignKey(d => d.RequesterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__friendshi__reque__17F790F9");
        });

        modelBuilder.Entity<ReportReasonConfig>(entity =>
        {
            entity.HasKey(e => e.ReasonCode).HasName("PK__report_r__3CA7EBEAB962CFAE");

            entity.ToTable("report_reason_configs");

            entity.Property(e => e.ReasonCode)
                .HasMaxLength(50)
                .HasColumnName("reason_code");
            entity.Property(e => e.AutoFlagThreshold)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("auto_flag_threshold");
            entity.Property(e => e.BaseScore)
                .HasColumnType("decimal(5, 2)")
                .HasColumnName("base_score");
            entity.Property(e => e.Description)
                .HasMaxLength(255)
                .HasColumnName("description");
            entity.Property(e => e.SeverityLevel)
                .HasMaxLength(20)
                .HasColumnName("severity_level");
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(e => e.TierId).HasName("PK__subscrip__9D52AF9C4432F8CD");

            entity.ToTable("subscriptions");

            entity.HasIndex(e => e.TierName, "UQ__subscrip__10845677F3F410E0").IsUnique();

            entity.Property(e => e.TierId).HasColumnName("tier_id");
            entity.Property(e => e.AiPromptLimitPerDay).HasColumnName("ai_prompt_limit_per_day");
            entity.Property(e => e.MaxStorageMb).HasColumnName("max_storage_mb");
            entity.Property(e => e.Price)
                .HasDefaultValue(0.00m)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("price");
            entity.Property(e => e.TierName)
                .HasMaxLength(50)
                .HasColumnName("tier_name");
            entity.Property(e => e.TotalStorageMb).HasColumnName("total_storage_mb");
        });

        modelBuilder.Entity<TransferConfiguration>(entity =>
        {
            entity.HasKey(e => e.ConfigurationId);
            entity.ToTable("transfer_configurations");
            entity.Property(e => e.ConfigurationId).HasColumnName("configuration_id");
            entity.Property(e => e.BankCode).HasMaxLength(30).HasColumnName("bank_code");
            entity.Property(e => e.BankName).HasMaxLength(100).HasColumnName("bank_name");
            entity.Property(e => e.AccountNumber).HasMaxLength(50).HasColumnName("account_number");
            entity.Property(e => e.AccountName).HasMaxLength(150).HasColumnName("account_name");
            entity.Property(e => e.QrTemplate).HasMaxLength(30).HasColumnName("qr_template");
            entity.Property(e => e.TransferContentPrefix).HasMaxLength(50).HasColumnName("transfer_content_prefix");
            entity.Property(e => e.IsActive).HasColumnName("is_active");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("(getdate())").HasColumnName("updated_at");
        });

        modelBuilder.Entity<ModerationAction>(entity =>
        {
            entity.HasKey(e => e.ActionId);
            entity.ToTable("moderation_actions");
            entity.Property(e => e.ActionId).HasColumnName("action_id");
            entity.Property(e => e.ActorUserId).HasColumnName("actor_user_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.ReportId).HasColumnName("report_id");
            entity.Property(e => e.Action).HasMaxLength(50).HasColumnName("action");
            entity.Property(e => e.PreviousStatus).HasMaxLength(30).HasColumnName("previous_status");
            entity.Property(e => e.NewStatus).HasMaxLength(30).HasColumnName("new_status");
            entity.Property(e => e.Note).HasMaxLength(1000).HasColumnName("note");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
            entity.HasOne(e => e.Actor).WithMany().HasForeignKey(e => e.ActorUserId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ModerationAppeal>(entity =>
        {
            entity.HasKey(e => e.AppealId);
            entity.ToTable("moderation_appeals");
            entity.HasIndex(e => e.ReportId).IsUnique();
            entity.Property(e => e.AppealId).HasColumnName("appeal_id");
            entity.Property(e => e.ReportId).HasColumnName("report_id");
            entity.Property(e => e.SubmittedByUserId).HasColumnName("submitted_by_user_id");
            entity.Property(e => e.Explanation).HasMaxLength(2000).HasColumnName("explanation");
            entity.Property(e => e.EvidenceUrl).HasMaxLength(1000).HasColumnName("evidence_url");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("PENDING").HasColumnName("status");
            entity.Property(e => e.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
            entity.Property(e => e.ReviewNote).HasMaxLength(1000).HasColumnName("review_note");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
            entity.Property(e => e.ReviewedAt).HasColumnName("reviewed_at");
            entity.HasOne(e => e.Report).WithMany().HasForeignKey(e => e.ReportId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ModerationNotice>(entity =>
        {
            entity.HasKey(e => e.NoticeId);
            entity.ToTable("moderation_notices");
            entity.Property(e => e.NoticeId).HasColumnName("notice_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.ReportId).HasColumnName("report_id");
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.RelatedUserId).HasColumnName("related_user_id");
            entity.Property(e => e.ActionUrl).HasMaxLength(500).HasColumnName("action_url");
            entity.Property(e => e.Type).HasMaxLength(50).HasColumnName("type");
            entity.Property(e => e.Title).HasMaxLength(200).HasColumnName("title");
            entity.Property(e => e.Message).HasMaxLength(1500).HasColumnName("message");
            entity.Property(e => e.CanAppeal).HasColumnName("can_appeal");
            entity.Property(e => e.IsRead).HasColumnName("is_read");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
        });

        modelBuilder.Entity<DocumentChunk>(entity =>
        {
            entity.HasKey(e => e.ChunkId);
            entity.ToTable("document_chunks");
            entity.HasIndex(e => new { e.DocumentId, e.ChunkIndex }).IsUnique();
            entity.Property(e => e.ChunkId).HasColumnName("chunk_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.ChunkIndex).HasColumnName("chunk_index");
            entity.Property(e => e.HeadingPath).HasColumnName("heading_path");
            entity.Property(e => e.PageNumber).HasColumnName("page_number");
            entity.Property(e => e.Text).HasColumnName("text");
            entity.Property(e => e.StartOffset).HasColumnName("start_offset");
            entity.Property(e => e.EndOffset).HasColumnName("end_offset");
            entity.Property(e => e.BoundingBoxX).HasColumnName("bounding_box_x");
            entity.Property(e => e.BoundingBoxY).HasColumnName("bounding_box_y");
            entity.Property(e => e.BoundingBoxWidth).HasColumnName("bounding_box_width");
            entity.Property(e => e.BoundingBoxHeight).HasColumnName("bounding_box_height");
            entity.Property(e => e.OcrConfidence).HasColumnName("ocr_confidence");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
            entity.HasOne(e => e.Document).WithMany(d => d.DocumentChunks)
                .HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Transaction>(entity =>
        {
            entity.HasKey(e => e.TransactionId).HasName("PK__transact__85C600AF9446ED91");

            entity.ToTable("transactions");

            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.Amount)
                .HasColumnType("decimal(10, 2)")
                .HasColumnName("amount");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");
            entity.Property(e => e.StartedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("started_at");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("PENDING")
                .HasColumnName("status");
            entity.Property(e => e.Type)
                .HasMaxLength(20)
                .HasColumnName("type");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.Property(e => e.ReferenceCode).HasMaxLength(100).HasColumnName("reference_code");
            entity.Property(e => e.BankId).HasMaxLength(50).HasColumnName("bank_id");
            entity.Property(e => e.ApproverId).HasColumnName("approver_id");
            entity.Property(e => e.FailureReason).HasMaxLength(500).HasColumnName("failure_reason");

            entity.HasOne(d => d.User).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__transacti__user___0B91BA14");

            entity.HasOne(d => d.Approver).WithMany()
                .HasForeignKey(d => d.ApproverId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PK__users__B9BE370FF7FCE835");

            entity.ToTable("users", tb => tb.HasTrigger("trg_users_updated_at"));

            entity.HasIndex(e => e.Email, "UQ__users__AB6E6164FC555B14").IsUnique();

            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.AiPromptsToday)
                .HasDefaultValue(0)
                .HasColumnName("ai_prompts_today");
            entity.Property(e => e.Balance)
                .HasDefaultValue(0)
                .HasColumnName("balance");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("created_at");
            entity.Property(e => e.DowngradeNoticePending).HasColumnName("downgrade_notice_pending");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .HasColumnName("email");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.ExpiryNotified).HasColumnName("expiry_notified");
            entity.Property(e => e.LastPromptReset)
                .HasDefaultValueSql("(getdate())")
                .HasColumnType("datetime")
                .HasColumnName("last_prompt_reset");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.Role)
                .HasMaxLength(10)
                .HasDefaultValue("STUDENT")
                .HasColumnName("role");
            entity.Property(e => e.Status)
                .HasMaxLength(10)
                .HasDefaultValue("ACTIVE")
                .HasColumnName("status");
            entity.Property(e => e.TierId)
                .HasDefaultValue(2)
                .HasColumnName("tier_id");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("updated_at");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");

            entity.Property(e => e.IsAutoRenew)
                .HasDefaultValue(true)
                .HasColumnName("is_auto_renew");
            entity.Property(e => e.GracePeriodEndsAt)
                .HasColumnName("grace_period_ends_at");
            entity.Property(e => e.BalanceVersion)
                .HasDefaultValue(0)
                .HasColumnName("balance_version")
                .IsConcurrencyToken();

            entity.HasOne(d => d.Tier).WithMany(p => p.Users)
                .HasForeignKey(d => d.TierId)
                .HasConstraintName("FK__users__tier_id__5BE2A6F2");
        });

        modelBuilder.Entity<BalanceLedger>(entity =>
        {
            entity.HasKey(e => e.LedgerId);
            entity.ToTable("balance_ledgers");
            entity.Property(e => e.LedgerId).HasColumnName("ledger_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.Amount).HasColumnType("decimal(10, 2)").HasColumnName("amount");
            entity.Property(e => e.PreviousBalance).HasColumnType("decimal(10, 2)").HasColumnName("previous_balance");
            entity.Property(e => e.CurrentBalance).HasColumnType("decimal(10, 2)").HasColumnName("current_balance");
            entity.Property(e => e.ActionType).HasMaxLength(20).HasColumnName("action_type");
            entity.Property(e => e.Description).HasMaxLength(500).HasColumnName("description");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
            entity.Property(e => e.Signature).HasMaxLength(256).HasColumnName("signature");

            entity.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(d => d.Transaction).WithMany().HasForeignKey(d => d.TransactionId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SubscriptionHistory>(entity =>
        {
            entity.HasKey(e => e.HistoryId);
            entity.ToTable("subscription_histories");
            entity.Property(e => e.HistoryId).HasColumnName("history_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.OldTierId).HasColumnName("old_tier_id");
            entity.Property(e => e.NewTierId).HasColumnName("new_tier_id");
            entity.Property(e => e.ChangeReason).HasMaxLength(100).HasColumnName("change_reason");
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("(getdate())").HasColumnName("changed_at");

            entity.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
