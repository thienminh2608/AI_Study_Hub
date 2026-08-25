using System;

namespace AIStudyHub.Application.DTOs;

public class DocumentVersionDto
{
    public int VersionId { get; set; }
    public int DocumentId { get; set; }
    public int VersionNumber { get; set; }
    public string CloudStorageUrl { get; set; } = null!;
    public string FileExtension { get; set; } = null!;
    public decimal FileSizeMb { get; set; }
    public string? ChangeSummary { get; set; }
    public int CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public bool IsCurrent { get; set; }
    public string AiParsingStatus { get; set; } = "PENDING";
}

public class CreateVersionRequest
{
    public string? ChangeSummary { get; set; }
}
