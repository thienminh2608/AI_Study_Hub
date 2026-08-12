using System;

namespace AIStudyHub.Application.DTOs;

public class DocumentResponseDto
{
    public int DocumentId { get; set; }
    public int UserId { get; set; }
    public string UploaderName { get; set; } = null!;
    public int? FolderId { get; set; }
    public string Title { get; set; } = null!;
    public string FileExtension { get; set; } = null!;
    public string CloudStorageUrl { get; set; } = null!;
    public decimal FileSizeMb { get; set; }
    public string AiParsingStatus { get; set; } = null!;
    public string SharingPermission { get; set; } = null!;
    public string? ShareLinkToken { get; set; }
    public decimal? TotalReportScore { get; set; }
    public bool? IsFlagged { get; set; }
    public int? BookmarkCount { get; set; }
    public int? DownloadCount { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class DocumentReportDto
{
    public int DocumentId { get; set; }
    public string ReasonCode { get; set; } = null!;
    public string? AdditionalDetails { get; set; }
}

public class DocumentReportResponseDto
{
    public int ReportId { get; set; }
    public int DocumentId { get; set; }
    public string DocumentTitle { get; set; } = null!;
    public int ReporterId { get; set; }
    public string ReporterName { get; set; } = null!;
    public string ReasonCode { get; set; } = null!;
    public string? AdditionalDetails { get; set; }
    public string Status { get; set; } = null!;
    public DateTime? CreatedAt { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string? ResolvedByAdminName { get; set; }
}
