using System;

namespace AIStudyHub.Domain.Entities;

public class FolderShare
{
    public long ShareId { get; set; }
    public int FolderId { get; set; }
    public int OwnerUserId { get; set; }
    public int SharedWithUserId { get; set; }
    public string Role { get; set; } = "VIEWER"; // VIEWER, EDITOR
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public virtual Folder Folder { get; set; } = null!;
    public virtual User SharedWithUser { get; set; } = null!;
}
