using System;

namespace AIStudyHub.Domain.Entities;

public class AuditLog
{
    public long AuditId { get; set; }
    public int ActorUserId { get; set; }
    public string Action { get; set; } = null!; // SHARE_ADDED, SHARE_REMOVED, ROLE_CHANGED, LINK_ROTATED, LINK_REVOKED, PERMISSION_CHANGED, ITEM_TRASHED, ITEM_RESTORED
    public string TargetType { get; set; } = null!; // DOCUMENT, FOLDER
    public int TargetId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public virtual User ActorUser { get; set; } = null!;
}
