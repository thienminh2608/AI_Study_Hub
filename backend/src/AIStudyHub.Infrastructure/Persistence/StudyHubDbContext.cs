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

    protected StudyHubDbContext(DbContextOptions options)
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

    public virtual DbSet<DocumentOcrRegion> DocumentOcrRegions
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
    public virtual DbSet<DocumentProcessingJob> DocumentProcessingJobs { get; set; }
    public virtual DbSet<AiUsage> AiUsages { get; set; }
    public virtual DbSet<SubjectCategory> SubjectCategories { get; set; }
    public virtual DbSet<ChatMessageCitation> ChatMessageCitations { get; set; }
    public virtual DbSet<RefreshTokenSession> RefreshTokenSessions { get; set; }
    public virtual DbSet<AuthOtpChallenge> AuthOtpChallenges { get; set; }
    public virtual DbSet<PasswordResetGrant> PasswordResetGrants { get; set; }
    public virtual DbSet<PaymentWebhookEvent> PaymentWebhookEvents { get; set; }
    public virtual DbSet<PaymentReconciliationCase> PaymentReconciliationCases { get; set; }
    public virtual DbSet<AuthOtpRateLimit> AuthOtpRateLimits { get; set; }

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
            entity.Property(e => e.AttachmentEpoch)
                .HasDefaultValue(0)
                .HasColumnName("attachment_epoch");
            entity.Property(e => e.ContextDocumentId).HasColumnName("context_document_id");
            entity.Property(e => e.ContextDocumentVersionId).HasColumnName("context_document_version_id");
            entity.Property(e => e.MessageKind)
                .HasMaxLength(30)
                .HasDefaultValue("USER_MESSAGE")
                .HasColumnName("message_kind");
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
            entity.Property(e => e.AttachedDocumentVersionId).HasColumnName("attached_document_version_id");
            entity.Property(e => e.CurrentAttachmentEpoch)
                .HasDefaultValue(0)
                .HasColumnName("current_attachment_epoch");
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
            entity.Property(e => e.AiParsingStatus).HasMaxLength(20).HasDefaultValue("PENDING").HasColumnName("ai_parsing_status");
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

            entity.HasIndex(e => new { e.DocumentId, e.DocumentVersionId }, "UQ_document_extracted_text_doc_ver")
                .IsUnique()
                .HasFilter("[document_version_id] IS NOT NULL");

            entity.HasIndex(e => e.DocumentId, "UQ_document_extracted_text_doc_legacy")
                .IsUnique()
                .HasFilter("[document_version_id] IS NULL");

            entity.HasIndex(e => e.DocumentId, "IX_document_extracted_text_doc_id");

            entity.Property(e => e.ExtractionId).HasColumnName("extraction_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("(getdate())")
                .HasColumnName("created_at");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.DocumentVersionId).HasColumnName("document_version_id");
            entity.Property(e => e.ExtractedText).HasColumnName("extracted_text");
            entity.Property(e => e.TotalPages).HasColumnName("total_pages");
            entity.Property(e => e.ReadablePages).HasColumnName("readable_pages");
            entity.Property(e => e.ExtractionCoverage).HasColumnType("decimal(5, 4)").HasColumnName("extraction_coverage");
            entity.Property(e => e.ImageContentDetected).HasColumnName("image_content_detected");
            entity.Property(e => e.UnreadImageContentWarning).HasColumnName("unread_image_content_warning");
            entity.Property(e => e.OcrRegionCount).HasColumnName("ocr_region_count");

            entity.HasOne(d => d.Document).WithMany(p => p.DocumentExtractedTexts)
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("FK__document___docum__10566F31");

            entity.HasOne(d => d.DocumentVersion).WithMany(v => v.DocumentExtractedTexts)
                .HasForeignKey(d => d.DocumentVersionId)
                .OnDelete(DeleteBehavior.NoAction)
                .HasConstraintName("FK_document_extracted_text_document_versions");
        });

            modelBuilder.Entity<DocumentOcrRegion>(entity =>
            {
                entity.HasKey(e => e.OcrRegionId);
                entity.ToTable("document_ocr_regions");
                entity.Property(e => e.OcrRegionId).HasColumnName("ocr_region_id");
                entity.Property(e => e.DocumentId).HasColumnName("document_id");
                entity.Property(e => e.PageNumber).HasColumnName("page_number");
                entity.Property(e => e.RegionType).HasMaxLength(30).HasColumnName("region_type");
                entity.Property(e => e.BoundingBoxLeft).HasColumnName("bounding_box_left");
                entity.Property(e => e.BoundingBoxTop).HasColumnName("bounding_box_top");
                entity.Property(e => e.BoundingBoxWidth).HasColumnName("bounding_box_width");
                entity.Property(e => e.BoundingBoxHeight).HasColumnName("bounding_box_height");
                entity.Property(e => e.Confidence).HasColumnType("decimal(5, 4)").HasColumnName("confidence");
                entity.Property(e => e.RecognizedText).HasColumnName("recognized_text");
                entity.Property(e => e.Source).HasMaxLength(30).HasColumnName("source");
                entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
                entity.HasIndex(e => new { e.DocumentId, e.PageNumber });
                entity.HasOne(e => e.Document).WithMany(d => d.DocumentOcrRegions)
                .HasForeignKey(e => e.DocumentId).OnDelete(DeleteBehavior.Cascade);
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
            entity.Property(e => e.ReportedVersionId).HasColumnName("reported_version_id");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValue("PENDING")
                .HasColumnName("status");

            entity.HasOne(d => d.Document).WithMany(p => p.DocumentReports)
                .HasForeignKey(d => d.DocumentId)
                .HasConstraintName("FK__document___docum__245D67DE");

            entity.HasOne(d => d.ReportedVersion).WithMany()
                .HasForeignKey(d => d.ReportedVersionId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.ReasonCodeNavigation).WithMany(p => p.DocumentReports)
                .HasForeignKey(d => d.ReasonCode)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__document___reaso__2645B050");

            entity.HasOne(d => d.Reporter).WithMany(p => p.DocumentReportReporters)
                .HasForeignKey(d => d.ReporterId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__document___repor__25518C17");

            entity.HasOne(d => d.AssignedModerator).WithMany()
                .HasForeignKey(d => d.AssignedModeratorId)
                .OnDelete(DeleteBehavior.NoAction);

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
            entity.HasOne(e => e.SubmittedByUser).WithMany().HasForeignKey(e => e.SubmittedByUserId).OnDelete(DeleteBehavior.NoAction);
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
            entity.HasIndex(e => new { e.DocumentId, e.DocumentVersionId, e.ChunkIndex }).IsUnique();
            entity.Property(e => e.ChunkId).HasColumnName("chunk_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.DocumentVersionId).HasColumnName("document_version_id");
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
            entity.HasOne(e => e.DocumentVersion).WithMany(v => v.DocumentChunks)
                .HasForeignKey(e => e.DocumentVersionId).OnDelete(DeleteBehavior.NoAction);
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
            entity.Property(e => e.OriginalTransactionId).HasColumnName("original_transaction_id");

            entity.Property(e => e.PayOsOrderCode).HasColumnName("payos_order_code");
            entity.Property(e => e.PaymentLinkId).HasMaxLength(100).HasColumnName("payment_link_id");
            entity.Property(e => e.ReconciliationLockedUntil).HasColumnType("datetime2").HasColumnName("reconciliation_locked_until");
            entity.Property(e => e.ReconciliationAttempts).HasColumnName("reconciliation_attempts");
            entity.Property(e => e.LastReconciliationAt).HasColumnType("datetime2").HasColumnName("last_reconciliation_at");
            entity.Property(e => e.RequiresManualReview).HasColumnName("requires_manual_review");
            entity.Property(e => e.ReviewReason).HasMaxLength(500).HasColumnName("review_reason");
            entity.Property(e => e.ExpectedAmount).HasColumnType("decimal(18, 2)").HasColumnName("expected_amount");
            entity.Property(e => e.ProviderReportedAmount).HasColumnType("decimal(18, 2)").HasColumnName("provider_reported_amount");

            entity.HasOne(d => d.User).WithMany(p => p.Transactions)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK__transacti__user___0B91BA14");

            entity.HasOne(d => d.Approver).WithMany()
                .HasForeignKey(d => d.ApproverId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.OriginalTransaction).WithMany()
                .HasForeignKey(d => d.OriginalTransactionId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(e => e.OriginalTransactionId).IsUnique()
                .HasFilter("original_transaction_id IS NOT NULL AND status = 'SUCCESS'");

            entity.HasIndex(e => e.PayOsOrderCode, "UQ_transactions_payos_order_code")
                .HasFilter("[payos_order_code] IS NOT NULL")
                .IsUnique();
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
            entity.Property(e => e.LedgerSequence).HasColumnName("ledger_sequence");
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.Amount).HasColumnType("decimal(10, 2)").HasColumnName("amount");
            entity.Property(e => e.PreviousBalance).HasColumnType("decimal(10, 2)").HasColumnName("previous_balance");
            entity.Property(e => e.CurrentBalance).HasColumnType("decimal(10, 2)").HasColumnName("current_balance");
            entity.Property(e => e.ActionType).HasMaxLength(30).HasColumnName("action_type");
            entity.Property(e => e.Description).HasMaxLength(500).HasColumnName("description");
            entity.Property(e => e.PreviousHash).HasMaxLength(256).HasColumnName("previous_hash");
            entity.Property(e => e.CurrentHash).HasMaxLength(256).HasColumnName("current_hash");
            entity.Property(e => e.HashVersion).HasDefaultValue(1).HasColumnName("hash_version");
            entity.Property(e => e.KeyVersion).HasDefaultValue(1).HasColumnName("key_version");
            entity.Property(e => e.CreatedAtUtc).HasDefaultValueSql("(getutcdate())").HasColumnName("created_at_utc");

            entity.HasIndex(e => new { e.UserId, e.LedgerSequence }).IsUnique();

            entity.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(d => d.Transaction).WithMany().HasForeignKey(d => d.TransactionId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<SubscriptionHistory>(entity =>
        {
            entity.HasKey(e => e.HistoryId);
            entity.ToTable("subscription_histories");
            entity.Property(e => e.HistoryId).HasColumnName("history_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.OldTierId).HasColumnName("old_tier_id");
            entity.Property(e => e.NewTierId).HasColumnName("new_tier_id");
            entity.Property(e => e.TierNameSnapshot).HasMaxLength(50).HasColumnName("tier_name_snapshot");
            entity.Property(e => e.PriceSnapshot).HasColumnType("decimal(18, 2)").HasDefaultValue(0m).HasColumnName("price_snapshot");
            entity.Property(e => e.CurrencySnapshot).HasMaxLength(10).HasDefaultValue("VND").HasColumnName("currency_snapshot");
            entity.Property(e => e.DurationDaysSnapshot).HasDefaultValue(30).HasColumnName("duration_days_snapshot");
            entity.Property(e => e.StorageLimitSnapshot).HasColumnName("storage_limit_snapshot");
            entity.Property(e => e.AiPromptLimitSnapshot).HasColumnName("ai_prompt_limit_snapshot");
            entity.Property(e => e.PricingPolicySnapshot).HasMaxLength(50).HasColumnName("pricing_policy_snapshot");
            entity.Property(e => e.PurchaseType).HasMaxLength(50).HasColumnName("purchase_type");
            entity.Property(e => e.ChangeReason).HasMaxLength(100).HasColumnName("change_reason");
            entity.Property(e => e.ChangedAt).HasDefaultValueSql("(getdate())").HasColumnName("changed_at");
            entity.Property(e => e.PurchasedAt).HasColumnName("purchased_at");
            entity.Property(e => e.EffectiveFrom).HasColumnName("effective_from");
            entity.Property(e => e.EffectiveUntil).HasColumnName("effective_until");

            entity.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(d => d.Transaction).WithMany().HasForeignKey(d => d.TransactionId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<DocumentProcessingJob>(entity =>
        {
            entity.HasKey(e => e.JobId);
            entity.ToTable("document_processing_jobs");
            entity.Property(e => e.JobId).HasColumnName("job_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.DocumentVersionId).HasColumnName("document_version_id");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("QUEUED").HasColumnName("status");
            entity.Property(e => e.AttemptCount).HasDefaultValue(0).HasColumnName("attempt_count");
            entity.Property(e => e.MaxAttempts).HasDefaultValue(3).HasColumnName("max_attempts");
            entity.Property(e => e.AvailableAt).HasDefaultValueSql("(getutcdate())").HasColumnName("available_at");
            entity.Property(e => e.LockedAt).HasColumnName("locked_at");
            entity.Property(e => e.LockedUntil).HasColumnName("locked_until");
            entity.Property(e => e.LockedBy).HasMaxLength(100).HasColumnName("locked_by");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("created_at");
            entity.Property(e => e.CompletedAt).HasColumnName("completed_at");

            entity.HasOne(d => d.Document).WithMany().HasForeignKey(d => d.DocumentId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(d => d.DocumentVersion).WithMany().HasForeignKey(d => d.DocumentVersionId).OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AiUsage>(entity =>
        {
            entity.HasKey(e => e.UsageId);
            entity.ToTable("ai_usages");
            entity.HasIndex(e => new { e.UserId, e.CreatedAt });
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.UsageId).HasColumnName("usage_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Provider).HasMaxLength(50).HasDefaultValue("Google").HasColumnName("provider");
            entity.Property(e => e.Model).HasMaxLength(100).HasColumnName("model");
            entity.Property(e => e.Operation).HasMaxLength(50).HasDefaultValue("CHAT").HasColumnName("operation");
            entity.Property(e => e.PromptTokens).HasColumnName("prompt_tokens");
            entity.Property(e => e.CompletionTokens).HasColumnName("completion_tokens");
            entity.Property(e => e.CachedTokens).HasDefaultValue(0).HasColumnName("cached_tokens");
            entity.Property(e => e.TotalTokens).HasColumnName("total_tokens");
            entity.Property(e => e.LatencyMs).HasColumnName("latency_ms");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("SUCCESS").HasColumnName("status");
            entity.Property(e => e.ErrorCode).HasMaxLength(100).HasColumnName("error_code");
            entity.Property(e => e.EstimatedCost).HasColumnType("decimal(18, 6)").HasDefaultValue(0m).HasColumnName("estimated_cost");
            entity.Property(e => e.Currency).HasMaxLength(10).HasDefaultValue("USD").HasColumnName("currency");
            entity.Property(e => e.PricingVersion).HasMaxLength(20).HasDefaultValue("2026.1").HasColumnName("pricing_version");
            entity.Property(e => e.RequestId).HasMaxLength(100).HasColumnName("request_id");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getutcdate())").HasColumnName("created_at");

            entity.HasOne(d => d.User).WithMany().HasForeignKey(d => d.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SubjectCategory>(entity =>
        {
            entity.HasKey(e => e.SubjectId);
            entity.ToTable("subject_categories");
            entity.Property(e => e.SubjectId).HasColumnName("subject_id");
            entity.Property(e => e.Name).HasMaxLength(100).HasColumnName("name");
            entity.Property(e => e.NormalizedName).HasMaxLength(100).HasColumnName("normalized_name");
            entity.Property(e => e.ParentSubjectId).HasColumnName("parent_subject_id");
            entity.Property(e => e.Depth).HasDefaultValue(0).HasColumnName("depth");
            entity.Property(e => e.SortOrder).HasDefaultValue(0).HasColumnName("sort_order");
            entity.Property(e => e.Status).HasMaxLength(20).HasDefaultValue("APPROVED").HasColumnName("status");
            entity.Property(e => e.RequestedByUserId).HasColumnName("requested_by_user_id");
            entity.Property(e => e.ApprovedByUserId).HasColumnName("approved_by_user_id");
            entity.Property(e => e.RejectionReason).HasMaxLength(500).HasColumnName("rejection_reason");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("(getdate())").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasOne(d => d.ParentSubject)
                .WithMany(p => p.ChildSubjects)
                .HasForeignKey(d => d.ParentSubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(d => d.RequestedByUser)
                .WithMany()
                .HasForeignKey(d => d.RequestedByUserId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.ApprovedByUser)
                .WithMany()
                .HasForeignKey(d => d.ApprovedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<ChatMessageCitation>(entity =>
        {
            entity.HasKey(e => e.CitationId);
            entity.ToTable("chat_message_citations");

            entity.Property(e => e.CitationId).HasColumnName("citation_id");
            entity.Property(e => e.MessageId).HasColumnName("message_id");
            entity.Property(e => e.DocumentId).HasColumnName("document_id");
            entity.Property(e => e.DocumentVersionId).HasColumnName("document_version_id");
            entity.Property(e => e.ChunkId).HasColumnName("chunk_id");
            entity.Property(e => e.DocumentTitleSnapshot).HasMaxLength(255).HasColumnName("document_title_snapshot");
            entity.Property(e => e.VersionNumberSnapshot).HasColumnName("version_number_snapshot");
            entity.Property(e => e.FileExtensionSnapshot).HasMaxLength(20).HasColumnName("file_extension_snapshot");
            entity.Property(e => e.PageNumberSnapshot).HasColumnName("page_number_snapshot");
            entity.Property(e => e.StartOffsetSnapshot).HasColumnName("start_offset_snapshot");
            entity.Property(e => e.EndOffsetSnapshot).HasColumnName("end_offset_snapshot");
            entity.Property(e => e.HeadingPathSnapshot).HasMaxLength(500).HasColumnName("heading_path_snapshot");
            entity.Property(e => e.Snippet).HasMaxLength(2000).HasColumnName("snippet");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("(sysutcdatetime())").HasColumnName("created_at");

            entity.HasIndex(e => new { e.MessageId, e.ChunkId })
                .HasFilter("[chunk_id] IS NOT NULL")
                .IsUnique();

            entity.HasOne(d => d.Message)
                .WithMany(p => p.Citations)
                .HasForeignKey(d => d.MessageId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Document)
                .WithMany(p => p.ChatMessageCitations)
                .HasForeignKey(d => d.DocumentId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.DocumentVersion)
                .WithMany(p => p.ChatMessageCitations)
                .HasForeignKey(d => d.DocumentVersionId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.Chunk)
                .WithMany(p => p.ChatMessageCitations)
                .HasForeignKey(d => d.ChunkId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<RefreshTokenSession>(entity =>
        {
            entity.HasKey(e => e.SessionId);
            entity.ToTable("refresh_token_sessions");

            entity.Property(e => e.SessionId).HasColumnName("session_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.TokenFamilyId).HasColumnName("token_family_id");
            entity.Property(e => e.ParentSessionId).HasColumnName("parent_session_id");
            entity.Property(e => e.TokenHash).HasMaxLength(128).HasColumnName("token_hash");
            entity.Property(e => e.ExpiresAt).HasColumnType("datetime2").HasColumnName("expires_at");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("(sysutcdatetime())").HasColumnName("created_at");
            entity.Property(e => e.CreatedByIp).HasMaxLength(45).HasColumnName("created_by_ip");
            entity.Property(e => e.UserAgent).HasMaxLength(500).HasColumnName("user_agent");
            entity.Property(e => e.RevokedAt).HasColumnType("datetime2").HasColumnName("revoked_at");
            entity.Property(e => e.RevokedReason).HasMaxLength(100).HasColumnName("revoked_reason");
            entity.Property(e => e.RevokedByIp).HasMaxLength(45).HasColumnName("revoked_by_ip");
            entity.Property(e => e.ReplacedByTokenHash).HasMaxLength(128).HasColumnName("replaced_by_token_hash");
            entity.Property(e => e.IsUsed).HasColumnName("is_used");
            entity.Property(e => e.LastUsedAt).HasColumnType("datetime2").HasColumnName("last_used_at");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");

            entity.HasIndex(e => e.TokenHash, "UQ_refresh_token_sessions_token_hash").IsUnique();
            entity.HasIndex(e => new { e.UserId, e.TokenFamilyId }, "IX_refresh_token_sessions_user_family");

            entity.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuthOtpChallenge>(entity =>
        {
            entity.HasKey(e => e.ChallengeId);
            entity.ToTable("auth_otp_challenges");

            entity.Property(e => e.ChallengeId).HasColumnName("challenge_id");
            entity.Property(e => e.NormalizedEmailHash).HasMaxLength(128).HasColumnName("normalized_email_hash");
            entity.Property(e => e.Purpose).HasMaxLength(50).HasColumnName("purpose");
            entity.Property(e => e.OtpHash).HasMaxLength(128).HasColumnName("otp_hash");
            entity.Property(e => e.Attempts).HasColumnName("attempts");
            entity.Property(e => e.MaxAttempts).HasColumnName("max_attempts");
            entity.Property(e => e.CooldownUntil).HasColumnType("datetime2").HasColumnName("cooldown_until");
            entity.Property(e => e.ExpiresAt).HasColumnType("datetime2").HasColumnName("expires_at");
            entity.Property(e => e.ConsumedAt).HasColumnType("datetime2").HasColumnName("consumed_at");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("(sysutcdatetime())").HasColumnName("created_at");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");

            entity.HasIndex(e => new { e.NormalizedEmailHash, e.Purpose }, "IX_auth_otp_challenges_email_purpose");
        });

        modelBuilder.Entity<PasswordResetGrant>(entity =>
        {
            entity.HasKey(e => e.GrantId);
            entity.ToTable("password_reset_grants");

            entity.Property(e => e.GrantId).HasColumnName("grant_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.ChallengeId).HasColumnName("challenge_id");
            entity.Property(e => e.GrantHash).HasMaxLength(128).HasColumnName("grant_hash");
            entity.Property(e => e.ExpiresAt).HasColumnType("datetime2").HasColumnName("expires_at");
            entity.Property(e => e.IsConsumed).HasColumnName("is_consumed");
            entity.Property(e => e.ConsumedAt).HasColumnType("datetime2").HasColumnName("consumed_at");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("(sysutcdatetime())").HasColumnName("created_at");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");

            entity.HasIndex(e => e.GrantHash, "UQ_password_reset_grants_hash").IsUnique();

            entity.HasOne(d => d.User)
                .WithMany()
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PaymentWebhookEvent>(entity =>
        {
            entity.HasKey(e => e.WebhookEventId);
            entity.ToTable("payment_webhook_events");

            entity.Property(e => e.WebhookEventId).HasColumnName("webhook_event_id");
            entity.Property(e => e.Provider).HasMaxLength(50).HasDefaultValue("PAYOS").HasColumnName("provider");
            entity.Property(e => e.ProviderEventId).HasMaxLength(150).HasColumnName("provider_event_id");
            entity.Property(e => e.MerchantOrderCode).HasColumnName("merchant_order_code");
            entity.Property(e => e.PayloadHash).HasMaxLength(128).HasColumnName("payload_hash");
            entity.Property(e => e.PayloadSanitized).HasColumnName("payload_sanitized");
            entity.Property(e => e.ExpectedAmount).HasColumnType("decimal(18, 2)").HasColumnName("expected_amount");
            entity.Property(e => e.ReceivedAmount).HasColumnType("decimal(18, 2)").HasColumnName("received_amount");
            entity.Property(e => e.Currency).HasMaxLength(10).HasColumnName("currency");
            entity.Property(e => e.RequiresManualReview).HasColumnName("requires_manual_review");
            entity.Property(e => e.ReviewReason).HasMaxLength(500).HasColumnName("review_reason");
            entity.Property(e => e.IsSyntheticReference).HasColumnName("is_synthetic_reference");
            entity.Property(e => e.ProcessedAt).HasColumnType("datetime2").HasColumnName("processed_at");
            entity.Property(e => e.Status).HasMaxLength(30).HasDefaultValue("RECEIVED").HasColumnName("status");
            entity.Property(e => e.ErrorMessage).HasMaxLength(1000).HasColumnName("error_message");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("(sysutcdatetime())").HasColumnName("created_at");
            entity.Property(e => e.RowVersion).IsRowVersion().HasColumnName("row_version");

            entity.HasIndex(e => new { e.Provider, e.ProviderEventId }, "UQ_payment_webhook_provider_event").IsUnique();
        });

        modelBuilder.Entity<PaymentReconciliationCase>(entity =>
        {
            entity.HasKey(e => e.CaseId);
            entity.ToTable("payment_reconciliation_cases");

            entity.Property(e => e.CaseId).HasColumnName("case_id");
            entity.Property(e => e.TransactionId).HasColumnName("transaction_id");
            entity.Property(e => e.PayOsOrderCode).HasColumnName("payos_order_code");
            entity.Property(e => e.Provider).HasMaxLength(50).HasDefaultValue("PAYOS").HasColumnName("provider");
            entity.Property(e => e.IssueType).HasMaxLength(50).HasColumnName("issue_type");
            entity.Property(e => e.ExpectedAmount).HasColumnType("decimal(18, 2)").HasColumnName("expected_amount");
            entity.Property(e => e.ProviderReportedAmount).HasColumnType("decimal(18, 2)").HasColumnName("provider_reported_amount");
            entity.Property(e => e.Currency).HasMaxLength(10).HasDefaultValue("VND").HasColumnName("currency");
            entity.Property(e => e.Details).HasColumnName("details");
            entity.Property(e => e.Status).HasMaxLength(30).HasDefaultValue("OPEN").HasColumnName("status");
            entity.Property(e => e.ResolvedAt).HasColumnType("datetime2").HasColumnName("resolved_at");
            entity.Property(e => e.ResolvedByUserId).HasColumnName("resolved_by_user_id");
            entity.Property(e => e.ResolutionNotes).HasColumnName("resolution_notes");
            entity.Property(e => e.CreatedAt).HasColumnType("datetime2").HasDefaultValueSql("(sysutcdatetime())").HasColumnName("created_at");

            entity.HasIndex(e => new { e.Status, e.CreatedAt }, "IX_payment_reconciliation_cases_status");

            entity.HasOne(d => d.Transaction)
                .WithMany()
                .HasForeignKey(d => d.TransactionId)
                .OnDelete(DeleteBehavior.NoAction);

            entity.HasOne(d => d.ResolvedByUser)
                .WithMany()
                .HasForeignKey(d => d.ResolvedByUserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<AuthOtpRateLimit>(entity =>
        {
            entity.HasKey(e => new { e.NormalizedEmailHash, e.Purpose });
            entity.ToTable("auth_otp_rate_limits");

            entity.Property(e => e.NormalizedEmailHash).HasMaxLength(128).HasColumnName("normalized_email_hash");
            entity.Property(e => e.Purpose).HasMaxLength(50).HasColumnName("purpose");
            entity.Property(e => e.CooldownUntil).HasColumnType("datetime2").HasColumnName("cooldown_until");
            entity.Property(e => e.LastSentAt).HasColumnType("datetime2").HasColumnName("last_sent_at");
            entity.Property(e => e.RequestCount).HasColumnName("request_count");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
