using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class Document
{
    public int DocumentId
    {
        get; set;
    }

    public int UserId
    {
        get; set;
    }

    public int? FolderId
    {
        get; set;
    }

    public string Title { get; set; } = null!;

    public string Subject { get; set; } = "Khác";

    public string FileExtension { get; set; } = null!;

    public string CloudStorageUrl { get; set; } = null!;

    public decimal FileSizeMb
    {
        get; set;
    }

    public string? AiParsingStatus
    {
        get; set;
    }

    public string? SharingPermission
    {
        get; set;
    }

    public string RequestedVisibility { get; set; } = "PRIVATE";
    public string ModerationStatus { get; set; } = "NOT_REQUESTED";
    public DateTime? ModerationSubmittedAt
    {
        get; set;
    }
    public DateTime? ModeratedAt
    {
        get; set;
    }
    public int? ModeratedByUserId
    {
        get; set;
    }
    public string? ModerationNote
    {
        get; set;
    }

    public string? ShareLinkToken
    {
        get; set;
    }

    public decimal? TotalReportScore
    {
        get; set;
    }

    public bool? IsFlagged
    {
        get; set;
    }

    public int? BookmarkCount
    {
        get; set;
    }

    public int? DownloadCount
    {
        get; set;
    }

    public int? ViewCount
    {
        get; set;
    }

    public DateTime? CreatedAt
    {
        get; set;
    }

    public DateTime? UpdatedAt
    {
        get; set;
    }

    public virtual ICollection<Bookmark> Bookmarks { get; set; } = new List<Bookmark>();

    public virtual DocumentExtractedText? DocumentExtractedText
    {
        get; set;
    }

    public virtual ICollection<DocumentChunk> DocumentChunks { get; set; } = new List<DocumentChunk>();

    public virtual ICollection<DocumentReport> DocumentReports { get; set; } = new List<DocumentReport>();

    public virtual ICollection<DocumentActivity> DocumentActivities { get; set; } = new List<DocumentActivity>();

    public virtual Folder? Folder
    {
        get; set;
    }

    public virtual User User { get; set; } = null!;
}
