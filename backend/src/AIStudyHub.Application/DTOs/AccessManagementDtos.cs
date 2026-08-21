using System;
using System.Collections.Generic;

namespace AIStudyHub.Application.DTOs;

public class UserShareDto
{
    public long ShareId { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Role { get; set; } = "VIEWER"; // VIEWER, EDITOR
    public DateTime CreatedAt { get; set; }
}

public class ShareLinkInfoDto
{
    public string? Token { get; set; }
    public string? ShareUrl { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public bool HasExpiration => ExpiresAt.HasValue;
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;
}

public class ItemAccessSettingsDto
{
    public int ItemId { get; set; }
    public string ItemType { get; set; } = null!; // DOCUMENT, FOLDER
    public string Title { get; set; } = null!;
    public int OwnerUserId { get; set; }
    public string OwnerName { get; set; } = null!;
    public string GeneralAccess { get; set; } = "RESTRICTED"; // RESTRICTED, LINK, PUBLIC
    public string? ModerationStatus { get; set; } // NOT_REQUESTED, PENDING_REVIEW, APPROVED, REJECTED, RESTRICTED
    public string? RequestedVisibility { get; set; }
    public string? SharingPermission { get; set; }
    public string? ModerationNote { get; set; }
    public DateTime? ModerationSubmittedAt { get; set; }
    public bool IsInherited { get; set; }
    public int? ParentFolderId { get; set; }
    public List<UserShareDto> Shares { get; set; } = new();
    public ShareLinkInfoDto ShareLink { get; set; } = new();
    public string UserEffectiveRole { get; set; } = "NONE"; // OWNER, EDITOR, VIEWER, NONE
}

public class UpdateGeneralAccessRequest
{
    public string GeneralAccess { get; set; } = "RESTRICTED"; // RESTRICTED, LINK, PUBLIC
}

public class AddUserShareRequest
{
    public string Email { get; set; } = null!;
    public string Role { get; set; } = "VIEWER"; // VIEWER, EDITOR
}

public class UpdateUserShareRoleRequest
{
    public string Role { get; set; } = "VIEWER";
}

public class CreateShareLinkRequest
{
    public DateTime? ExpiresAt { get; set; }
}

public class AuditLogDto
{
    public long AuditId { get; set; }
    public int ActorUserId { get; set; }
    public string ActorName { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string TargetType { get; set; } = null!;
    public int TargetId { get; set; }
    public string? Details { get; set; }
    public DateTime CreatedAt { get; set; }
}
