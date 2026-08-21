using System;

namespace AIStudyHub.Domain.Entities;

public class ChatMessageCitation
{
    public long CitationId { get; set; }
    public int MessageId { get; set; }
    public int DocumentId { get; set; }
    public int DocumentVersionId { get; set; }
    public int? ChunkId { get; set; }

    public string DocumentTitleSnapshot { get; set; } = string.Empty;
    public int VersionNumberSnapshot { get; set; }
    public string FileExtensionSnapshot { get; set; } = string.Empty;
    public int? PageNumberSnapshot { get; set; }
    public int? StartOffsetSnapshot { get; set; }
    public int? EndOffsetSnapshot { get; set; }
    public string? HeadingPathSnapshot { get; set; }
    public string Snippet { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ChatMessage Message { get; set; } = null!;
    public virtual Document Document { get; set; } = null!;
    public virtual DocumentVersion DocumentVersion { get; set; } = null!;
    public virtual DocumentChunk? Chunk { get; set; }
}
