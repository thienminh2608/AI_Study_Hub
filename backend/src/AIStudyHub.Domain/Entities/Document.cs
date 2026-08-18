using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class Document
{
    public int DocumentId { get; set; }
    public int UserId { get; set; }
    public int? FolderId { get; set; }
    public string Title { get; set; } = null!;
    public string Subject { get; set; } = "Khác";
    public string FileExtension { get; set; } = null!;
    public string CloudStorageUrl { get; set; } = null!;
    public decimal FileSizeMb { get; set; }
    public string? AiParsingStatus { get; set; }
    public string? SharingPermission { get; set; }

    // General Access: RESTRICTED, LINK, PUBLIC
    public string GeneralAccess { get; set; } = "RESTRICTED";
    public string LifeCycleStatus { get; set; } = "PRIVATE"; // DRAFT, PRIVATE, PENDING_APPROVAL, APPROVED, REJECTED, TRASHED

    public string RequestedVisibility { get; set; } = "PRIVATE";
    public string ModerationStatus { get; set; } = "NOT_REQUESTED";
    public DateTime? ModerationSubmittedAt { get; set; }
    public DateTime? ModeratedAt { get; set; }
    public int? ModeratedByUserId { get; set; }
    public string? ModerationNote { get; set; }

    public string? ShareLinkToken { get; set; }
    public DateTime? ShareLinkExpiresAt { get; set; }
    public bool IsShareLinkRevoked { get; set; } = false;

    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public int? DeletedByUserId { get; set; }

    public int? CurrentVersionId { get; set; }

    public decimal? TotalReportScore { get; set; }
    public bool? IsFlagged { get; set; }
    public int? BookmarkCount { get; set; }
    public int? DownloadCount { get; set; }
    public int? ViewCount { get; set; }
    public double? ExtractionCoveragePercent { get; set; }

    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public virtual ICollection<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();
    public virtual DocumentExtractedText? DocumentExtractedText { get; set; }
    public virtual ICollection<DocumentOcrRegion> DocumentOcrRegions { get; set; } = new List<DocumentOcrRegion>();
    public virtual ICollection<DocumentChunk> DocumentChunks { get; set; } = new List<DocumentChunk>();
    public virtual ICollection<DocumentReport> DocumentReports { get; set; } = new List<DocumentReport>();
    public virtual ICollection<DocumentActivity> DocumentActivities { get; set; } = new List<DocumentActivity>();
    public virtual ICollection<DocumentShare> DocumentShares { get; set; } = new List<DocumentShare>();
    public virtual ICollection<DocumentVersion> DocumentVersions { get; set; } = new List<DocumentVersion>();
    public virtual Folder? Folder { get; set; }
    public virtual User User { get; set; } = null!;
}
