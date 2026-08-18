using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Application.Services;

public class PermissionService : IPermissionService
{
    private readonly IStudyHubDbContext _db;

    public PermissionService(IStudyHubDbContext db)
    {
        _db = db;
    }

    public async Task<string> GetEffectiveDocumentRoleAsync(int documentId, int userId, string? shareToken = null)
    {
        var doc = await _db.Documents
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);

        if (doc == null || doc.IsDeleted) return "NONE";

        // 1. Owner
        if (doc.UserId == userId) return "OWNER";

        // 2. Direct User Share on Document
        var directShare = await _db.DocumentShares
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.DocumentId == documentId && s.SharedWithUserId == userId);
        if (directShare != null) return directShare.Role;

        // 3. Inherited Share from Parent Folders
        if (doc.FolderId.HasValue)
        {
            var folderRole = await GetEffectiveFolderRoleAsync(doc.FolderId.Value, userId);
            if (folderRole == "OWNER" || folderRole == "EDITOR" || folderRole == "VIEWER")
            {
                return folderRole == "OWNER" ? "EDITOR" : folderRole;
            }
        }

        // 4. Share Link Token Access
        if (!string.IsNullOrWhiteSpace(shareToken) && doc.ShareLinkToken == shareToken && !doc.IsShareLinkRevoked)
        {
            if (!doc.ShareLinkExpiresAt.HasValue || doc.ShareLinkExpiresAt.Value > DateTime.UtcNow)
            {
                return "VIEWER";
            }
        }

        // 5. General Access
        if (doc.GeneralAccess == "LINK" && !doc.IsShareLinkRevoked) return "VIEWER";
        if (doc.GeneralAccess == "PUBLIC") return "VIEWER";

        return "NONE";
    }

    public async Task<string> GetEffectiveFolderRoleAsync(int folderId, int userId)
    {
        int? currentFolderId = folderId;

        while (currentFolderId.HasValue)
        {
            var folder = await _db.Folders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.FolderId == currentFolderId.Value);

            if (folder == null || folder.IsDeleted) break;

            if (folder.UserId == userId) return "OWNER";

            var share = await _db.FolderShares
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.FolderId == currentFolderId.Value && s.SharedWithUserId == userId);

            if (share != null) return share.Role;

            if (folder.GeneralAccess == "PUBLIC" || folder.GeneralAccess == "LINK") return "VIEWER";

            currentFolderId = folder.ParentFolderId;
        }

        return "NONE";
    }

    public async Task<ItemAccessSettingsDto> GetDocumentAccessSettingsAsync(int documentId, int currentUserId)
    {
        var doc = await _db.Documents
            .Include(d => d.User)
            .Include(d => d.DocumentShares)
                .ThenInclude(s => s.SharedWithUser)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);

        if (doc == null) throw new KeyNotFoundException("Document not found");

        var effectiveRole = await GetEffectiveDocumentRoleAsync(documentId, currentUserId);

        var shares = doc.DocumentShares.Select(s => new UserShareDto
        {
            ShareId = s.ShareId,
            UserId = s.SharedWithUserId,
            Username = s.SharedWithUser.Username,
            Email = s.SharedWithUser.Email ?? "",
            Role = s.Role,
            CreatedAt = s.CreatedAt
        }).ToList();

        return new ItemAccessSettingsDto
        {
            ItemId = doc.DocumentId,
            ItemType = "DOCUMENT",
            Title = doc.Title,
            OwnerUserId = doc.UserId,
            OwnerName = doc.User.Username,
            GeneralAccess = doc.GeneralAccess,
            IsInherited = doc.FolderId.HasValue,
            ParentFolderId = doc.FolderId,
            Shares = shares,
            ShareLink = new ShareLinkInfoDto
            {
                Token = doc.ShareLinkToken,
                ShareUrl = !string.IsNullOrEmpty(doc.ShareLinkToken) ? $"/d/{doc.ShareLinkToken}" : null,
                ExpiresAt = doc.ShareLinkExpiresAt,
                IsRevoked = doc.IsShareLinkRevoked
            },
            UserEffectiveRole = effectiveRole
        };
    }

    public async Task<ItemAccessSettingsDto> GetFolderAccessSettingsAsync(int folderId, int currentUserId)
    {
        var folder = await _db.Folders
            .Include(f => f.User)
            .Include(f => f.FolderShares)
                .ThenInclude(s => s.SharedWithUser)
            .FirstOrDefaultAsync(f => f.FolderId == folderId);

        if (folder == null) throw new KeyNotFoundException("Folder not found");

        var effectiveRole = await GetEffectiveFolderRoleAsync(folderId, currentUserId);

        var shares = folder.FolderShares.Select(s => new UserShareDto
        {
            ShareId = s.ShareId,
            UserId = s.SharedWithUserId,
            Username = s.SharedWithUser.Username,
            Email = s.SharedWithUser.Email ?? "",
            Role = s.Role,
            CreatedAt = s.CreatedAt
        }).ToList();

        return new ItemAccessSettingsDto
        {
            ItemId = folder.FolderId,
            ItemType = "FOLDER",
            Title = folder.FolderName,
            OwnerUserId = folder.UserId,
            OwnerName = folder.User.Username,
            GeneralAccess = folder.GeneralAccess,
            IsInherited = folder.ParentFolderId.HasValue,
            ParentFolderId = folder.ParentFolderId,
            Shares = shares,
            UserEffectiveRole = effectiveRole
        };
    }

    public async Task UpdateDocumentGeneralAccessAsync(int documentId, string generalAccess, int currentUserId)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null) throw new KeyNotFoundException("Document not found");
        if (doc.UserId != currentUserId) throw new UnauthorizedAccessException("Only document owner can change access settings");

        string oldAccess = doc.GeneralAccess;
        doc.GeneralAccess = generalAccess;
        await _db.SaveChangesAsync();

        await LogAuditAsync(currentUserId, "GENERAL_ACCESS_CHANGED", "DOCUMENT", documentId, $"Changed from {oldAccess} to {generalAccess}");
    }

    public async Task UpdateFolderGeneralAccessAsync(int folderId, string generalAccess, int currentUserId)
    {
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.FolderId == folderId);
        if (folder == null) throw new KeyNotFoundException("Folder not found");
        if (folder.UserId != currentUserId) throw new UnauthorizedAccessException("Only folder owner can change access settings");

        string oldAccess = folder.GeneralAccess;
        folder.GeneralAccess = generalAccess;
        await _db.SaveChangesAsync();

        await LogAuditAsync(currentUserId, "GENERAL_ACCESS_CHANGED", "FOLDER", folderId, $"Changed from {oldAccess} to {generalAccess}");
    }

    public async Task AddOrUpdateDocumentUserShareAsync(int documentId, string email, string role, int currentUserId)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null) throw new KeyNotFoundException("Document not found");
        if (doc.UserId != currentUserId) throw new UnauthorizedAccessException("Only owner can modify shares");

        string cleanEmail = email.Trim().ToLower();
        var targetUser = await _db.Users.FirstOrDefaultAsync(u => (u.Email != null && u.Email.ToLower() == cleanEmail) || u.Username.ToLower() == cleanEmail);
        if (targetUser == null) throw new KeyNotFoundException($"Không tìm thấy người dùng với Email/Username '{email}'");
        if (targetUser.UserId == currentUserId) throw new InvalidOperationException("Cannot share document with yourself");

        var existingShare = await _db.DocumentShares.FirstOrDefaultAsync(s => s.DocumentId == documentId && s.SharedWithUserId == targetUser.UserId);
        if (existingShare != null)
        {
            existingShare.Role = role;
            await LogAuditAsync(currentUserId, "ROLE_CHANGED", "DOCUMENT", documentId, $"Updated role for {email} to {role}");
        }
        else
        {
            _db.DocumentShares.Add(new DocumentShare
            {
                DocumentId = documentId,
                OwnerUserId = currentUserId,
                SharedWithUserId = targetUser.UserId,
                Role = role,
                CreatedAt = DateTime.Now
            });
            await LogAuditAsync(currentUserId, "SHARE_ADDED", "DOCUMENT", documentId, $"Shared with {email} as {role}");
        }
        await _db.SaveChangesAsync();
    }

    public async Task AddOrUpdateFolderUserShareAsync(int folderId, string email, string role, int currentUserId)
    {
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.FolderId == folderId);
        if (folder == null) throw new KeyNotFoundException("Folder not found");
        if (folder.UserId != currentUserId) throw new UnauthorizedAccessException("Only owner can modify shares");

        string cleanEmail = email.Trim().ToLower();
        var targetUser = await _db.Users.FirstOrDefaultAsync(u => (u.Email != null && u.Email.ToLower() == cleanEmail) || u.Username.ToLower() == cleanEmail);
        if (targetUser == null) throw new KeyNotFoundException($"Không tìm thấy người dùng với Email/Username '{email}'");
        if (targetUser.UserId == currentUserId) throw new InvalidOperationException("Cannot share folder with yourself");

        var existingShare = await _db.FolderShares.FirstOrDefaultAsync(s => s.FolderId == folderId && s.SharedWithUserId == targetUser.UserId);
        if (existingShare != null)
        {
            existingShare.Role = role;
            await LogAuditAsync(currentUserId, "ROLE_CHANGED", "FOLDER", folderId, $"Updated role for {email} to {role}");
        }
        else
        {
            _db.FolderShares.Add(new FolderShare
            {
                FolderId = folderId,
                OwnerUserId = currentUserId,
                SharedWithUserId = targetUser.UserId,
                Role = role,
                CreatedAt = DateTime.Now
            });
            await LogAuditAsync(currentUserId, "SHARE_ADDED", "FOLDER", folderId, $"Shared folder with {email} as {role}");
        }
        await _db.SaveChangesAsync();
    }

    public async Task RemoveDocumentUserShareAsync(int documentId, int targetUserId, int currentUserId)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null) throw new KeyNotFoundException("Document not found");
        if (doc.UserId != currentUserId) throw new UnauthorizedAccessException("Only owner can remove shares");

        var existingShare = await _db.DocumentShares.FirstOrDefaultAsync(s => s.DocumentId == documentId && s.SharedWithUserId == targetUserId);
        if (existingShare != null)
        {
            _db.DocumentShares.Remove(existingShare);
            await _db.SaveChangesAsync();
            await LogAuditAsync(currentUserId, "SHARE_REMOVED", "DOCUMENT", documentId, $"Removed share for user ID {targetUserId}");
        }
    }

    public async Task RemoveFolderUserShareAsync(int folderId, int targetUserId, int currentUserId)
    {
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.FolderId == folderId);
        if (folder == null) throw new KeyNotFoundException("Folder not found");
        if (folder.UserId != currentUserId) throw new UnauthorizedAccessException("Only owner can remove shares");

        var existingShare = await _db.FolderShares.FirstOrDefaultAsync(s => s.FolderId == folderId && s.SharedWithUserId == targetUserId);
        if (existingShare != null)
        {
            _db.FolderShares.Remove(existingShare);
            await _db.SaveChangesAsync();
            await LogAuditAsync(currentUserId, "SHARE_REMOVED", "FOLDER", folderId, $"Removed folder share for user ID {targetUserId}");
        }
    }

    public async Task<ShareLinkInfoDto> RotateDocumentShareLinkAsync(int documentId, int currentUserId)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null) throw new KeyNotFoundException("Document not found");
        if (doc.UserId != currentUserId) throw new UnauthorizedAccessException("Only owner can rotate share link");

        doc.ShareLinkToken = Guid.NewGuid().ToString("N");
        doc.IsShareLinkRevoked = false;
        await _db.SaveChangesAsync();

        await LogAuditAsync(currentUserId, "LINK_ROTATED", "DOCUMENT", documentId, $"New share token generated: {doc.ShareLinkToken}");

        return new ShareLinkInfoDto
        {
            Token = doc.ShareLinkToken,
            ShareUrl = $"/d/{doc.ShareLinkToken}",
            ExpiresAt = doc.ShareLinkExpiresAt,
            IsRevoked = doc.IsShareLinkRevoked
        };
    }

    public async Task RevokeDocumentShareLinkAsync(int documentId, int currentUserId)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null) throw new KeyNotFoundException("Document not found");
        if (doc.UserId != currentUserId) throw new UnauthorizedAccessException("Only owner can revoke share link");

        doc.IsShareLinkRevoked = true;
        await _db.SaveChangesAsync();

        await LogAuditAsync(currentUserId, "LINK_REVOKED", "DOCUMENT", documentId, "Share link revoked");
    }

    public async Task LogAuditAsync(int actorUserId, string action, string targetType, int targetId, string? details)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            ActorUserId = actorUserId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            Details = details,
            CreatedAt = DateTime.Now
        });
        await _db.SaveChangesAsync();
    }

    public async Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(int pageNumber, int pageSize)
    {
        var query = _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.ActorUser)
            .OrderByDescending(a => a.CreatedAt);

        int totalCount = await query.CountAsync();
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AuditLogDto
            {
                AuditId = a.AuditId,
                ActorUserId = a.ActorUserId,
                ActorName = a.ActorUser.Username,
                Action = a.Action,
                TargetType = a.TargetType,
                TargetId = a.TargetId,
                Details = a.Details,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return new PagedResult<AuditLogDto>(items, totalCount, pageNumber, pageSize);
    }
}
