namespace AIStudyHub.Domain.Entities;

public class DocumentShare
{
    public long ShareId { get; set; }
    public int DocumentId { get; set; }
    public int OwnerUserId { get; set; }
    public int SharedWithUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public Document Document { get; set; } = null!;
    public User SharedWithUser { get; set; } = null!;
}
