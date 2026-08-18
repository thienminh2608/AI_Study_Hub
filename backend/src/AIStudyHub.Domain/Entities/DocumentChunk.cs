namespace AIStudyHub.Domain.Entities;

public class DocumentChunk
{
    public int ChunkId
    {
        get; set;
    }
    public int DocumentId
    {
        get; set;
    }
    public int ChunkIndex
    {
        get; set;
    }
    public string? HeadingPath
    {
        get; set;
    }
    public int? PageNumber
    {
        get; set;
    }
    public string Text { get; set; } = null!;
    public int StartOffset
    {
        get; set;
    }
    public int EndOffset
    {
        get; set;
    }
    public double? BoundingBoxX { get; set; }
    public double? BoundingBoxY { get; set; }
    public double? BoundingBoxWidth { get; set; }
    public double? BoundingBoxHeight { get; set; }
    public double? OcrConfidence { get; set; }
    public DateTime CreatedAt
    {
        get; set;
    }

    public virtual Document Document { get; set; } = null!;
}
