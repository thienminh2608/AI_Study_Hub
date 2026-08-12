using System;
using System.Collections.Generic;

namespace AIStudyHub.Domain.Entities;

public partial class DocumentExtractedText
{
    public int ExtractionId { get; set; }

    public int DocumentId { get; set; }

    public string ExtractedText { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual Document Document { get; set; } = null!;
}
