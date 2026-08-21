using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class DocumentExtractedText
{
    public int ExtractionId
    {
        get; set;
    }

    public int DocumentId
    {
        get; set;
    }

    public int? DocumentVersionId
    {
        get; set;
    }

    public string ExtractedText { get; set; } = null!;

    public int TotalPages { get; set; }
    public int ReadablePages { get; set; }
    public decimal ExtractionCoverage { get; set; }
    public bool ImageContentDetected { get; set; }
    public bool UnreadImageContentWarning { get; set; }
    public int OcrRegionCount { get; set; }

    public DateTime? CreatedAt
    {
        get; set;
    }

    public virtual Document Document { get; set; } = null!;
    public virtual DocumentVersion? DocumentVersion { get; set; }
}
