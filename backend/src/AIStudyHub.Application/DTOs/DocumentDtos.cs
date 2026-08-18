using System;

namespace AIStudyHub.Application.DTOs;

public class DocumentResponseDto
{
    public int DocumentId
    {
        get; set;
    }
    public int UserId
    {
        get; set;
    }
    public string UploaderName { get; set; } = null!;
    public int? FolderId
    {
        get; set;
    }
    public string Title { get; set; } = null!;
    public string Subject { get; set; } = "Khác";
    public string FileExtension { get; set; } = null!;
    public string CloudStorageUrl { get; set; } = null!;
    public bool FileAvailable
    {
        get; set;
    }
    public decimal FileSizeMb
    {
        get; set;
    }
    public string AiParsingStatus { get; set; } = null!;
    public string SharingPermission { get; set; } = null!;
    public string RequestedVisibility { get; set; } = "PRIVATE";
    public string ModerationStatus { get; set; } = "NOT_REQUESTED";
    public string? ModerationNote
    {
        get; set;
    }
    public DateTime? ModerationSubmittedAt
    {
        get; set;
    }
    public DateTime? ModeratedAt
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
    public bool RequiresAppeal { get; set; }
    public bool PublicReviewBlocked { get; set; }
    public string? AppealStatus { get; set; }
    public decimal ExtractionCoverage { get; set; }
    public bool ImageContentDetected { get; set; }
    public bool UnreadImageContentWarning { get; set; }
    public int OcrRegionCount { get; set; }
}

public class DocumentAudienceDto
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
    public int DownloadCount
    {
        get; set;
    }
    public int ViewCount
    {
        get; set;
    }
    public DateTime LastActivityAt
    {
        get; set;
    }
}

public class DocumentAnalyticsDto
{
    public int TotalDocuments
    {
        get; set;
    }
    public int PublicDocuments
    {
        get; set;
    }
    public int PrivateDocuments
    {
        get; set;
    }
    public int TotalDownloads
    {
        get; set;
    }
    public int TotalViews
    {
        get; set;
    }
    public int TotalBookmarks
    {
        get; set;
    }
    public List<DocumentResponseDto> Documents { get; set; } = [];
    public int PendingReviewCount { get; set; }
    public List<DocumentResponseDto> PendingReviewDocuments { get; set; } = [];
}

public class DocumentDetailDto
{
    public DocumentResponseDto Document { get; set; } = null!;
    public string Description { get; set; } = "Chưa có mô tả.";
    public List<DocumentAudienceDto> Audience { get; set; } = [];
}

public class DocumentReportDto
{
    public int DocumentId
    {
        get; set;
    }
    public string ReasonCode { get; set; } = null!;
    public string? AdditionalDetails
    {
        get; set;
    }
    public string ReportType { get; set; } = "COMMUNITY";
    public string? ClaimantName
    {
        get; set;
    }
    public string? ClaimantEmail
    {
        get; set;
    }
    public string? OriginalWorkUrl
    {
        get; set;
    }
    public string? EvidenceDescription
    {
        get; set;
    }
    public bool InformationConfirmed
    {
        get; set;
    }
}

public class DocumentReportResponseDto
{
    public int ReportId
    {
        get; set;
    }
    public int DocumentId
    {
        get; set;
    }
    public string DocumentTitle { get; set; } = null!;
    public int ReporterId
    {
        get; set;
    }
    public string ReporterName { get; set; } = null!;
    public string ReasonCode { get; set; } = null!;
    public string? AdditionalDetails
    {
        get; set;
    }
    public string Status { get; set; } = null!;
    public DateTime? CreatedAt
    {
        get; set;
    }
    public DateTime? ResolvedAt
    {
        get; set;
    }
    public string? ResolvedByAdminName
    {
        get; set;
    }
    public string ReportType { get; set; } = "COMMUNITY";
    public string? ClaimantName
    {
        get; set;
    }
    public string? ClaimantEmail
    {
        get; set;
    }
    public string? OriginalWorkUrl
    {
        get; set;
    }
    public string? EvidenceDescription
    {
        get; set;
    }
    public string? ModeratorNote
    {
        get; set;
    }
    public int? AssignedModeratorId
    {
        get; set;
    }
    public string? PreviousSharingPermission
    {
        get; set;
    }
    public DateTime? RestrictedAt
    {
        get; set;
    }
}

public class PublicReportReasonDto
{
    public string ReasonCode { get; set; } = null!;
    public string SeverityLevel { get; set; } = null!;
    public string Description { get; set; } = null!;
}
