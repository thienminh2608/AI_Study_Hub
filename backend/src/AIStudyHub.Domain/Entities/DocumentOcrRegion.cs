using System;

namespace AIStudyHub.Domain.Entities;

public class DocumentOcrRegion
{
    public long OcrRegionId { get; set; }
    public int DocumentId { get; set; }
    public int PageNumber { get; set; }
    public string RegionType { get; set; } = "IMAGE";
    public double BoundingBoxLeft { get; set; }
    public double BoundingBoxTop { get; set; }
    public double BoundingBoxWidth { get; set; }
    public double BoundingBoxHeight { get; set; }
    public decimal? Confidence { get; set; }
    public string? RecognizedText { get; set; }
    public string Source { get; set; } = "OCR";
    public DateTime CreatedAt { get; set; }

    public virtual Document Document { get; set; } = null!;
}