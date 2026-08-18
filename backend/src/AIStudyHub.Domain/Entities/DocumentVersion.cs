using System;

namespace AIStudyHub.Domain.Entities;

public class DocumentVersion
{
    public int VersionId { get; set; }
    public int DocumentId { get; set; }
    public int VersionNumber { get; set; }
    public string CloudStorageUrl { get; set; } = null!;
    public string FileExtension { get; set; } = null!;
    public decimal FileSizeMb { get; set; }
    public string? ChangeSummary { get; set; }
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public virtual Document Document { get; set; } = null!;
    public virtual User CreatedByUser { get; set; } = null!;
}
