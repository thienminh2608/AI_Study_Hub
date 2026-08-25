using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Application.Services;

public class PermissionService : IPermissionService
{
    private readonly IStudyHubDbContext _db;
    private readonly IMailService? _mailService;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<PermissionService>? _logger;

    public PermissionService(
        IStudyHubDbContext db,
        IMailService? mailService = null,
        IConfiguration? configuration = null,
        ILogger<PermissionService>? logger = null)
    {
        _db = db;
        _mailService = mailService;
        _configuration = configuration;
        _logger = logger;
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
        if (userId > 0)
        {
            var directShare = await _db.DocumentShares
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.DocumentId == documentId && s.SharedWithUserId == userId);
            if (directShare != null) return directShare.Role;
        }

        // 3. Inherited Share from Parent Folders
        if (doc.FolderId.HasValue && userId > 0)
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

        // 5. General Access / Public
        if (doc.SharingPermission == "PUBLIC" && doc.IsFlagged != true) return "VIEWER";
        if (doc.GeneralAccess == "PUBLIC" && doc.IsFlagged != true) return "VIEWER";

        return "NONE";
    }

    public async Task<string> GetEffectiveFolderRoleAsync(int folderId, int userId)
    {
        if (userId <= 0) return "NONE";

        int? currentFolderId = folderId;
        var visited = new HashSet<int>();
        int depth = 0;
        const int maxDepth = 20;

        while (currentFolderId.HasValue && depth < maxDepth)
        {
            if (!visited.Add(currentFolderId.Value))
            {
                // Cycle detected in folder hierarchy
                break;
            }
            depth++;

            var folder = await _db.Folders
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.FolderId == currentFolderId.Value);

            if (folder == null || folder.IsDeleted) break;

            if (folder.UserId == userId) return "OWNER";

            var share = await _db.FolderShares
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.FolderId == currentFolderId.Value && s.SharedWithUserId == userId);

            if (share != null) return share.Role;

            if (folder.GeneralAccess == "PUBLIC") return "VIEWER";

            currentFolderId = folder.ParentFolderId;
        }

        return "NONE";
    }

    public async Task<bool> CanViewDocumentAsync(int documentId, int userId, string? shareToken = null)
    {
        if (userId > 0)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
            if (user != null && (user.Role == "ADMIN" || user.Role == "MODERATOR"))
            {
                return true;
            }
        }

        var role = await GetEffectiveDocumentRoleAsync(documentId, userId, shareToken);
        return role == "OWNER" || role == "EDITOR" || role == "VIEWER";
    }

    public async Task<bool> CanDownloadDocumentAsync(int documentId, int userId, string? shareToken = null)
    {
        return await CanViewDocumentAsync(documentId, userId, shareToken);
    }

    public async Task<bool> CanEditDocumentAsync(int documentId, int userId)
    {
        if (userId <= 0) return false;

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
        if (user != null && user.Role == "ADMIN")
        {
            return true;
        }

        var role = await GetEffectiveDocumentRoleAsync(documentId, userId);
        return role == "OWNER" || role == "EDITOR";
    }

    public async Task<bool> CanManageDocumentAccessAsync(int documentId, int userId)
    {
        if (userId <= 0) return false;

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId);
        if (user != null && (user.Role == "ADMIN" || user.Role == "MODERATOR"))
        {
            return true;
        }

        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.DocumentId == documentId && !d.IsDeleted);
        if (doc == null) return false;

        return doc.UserId == userId;
    }

    public async Task<List<int>> GetSharedDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null)
    {
        if (userId <= 0) return new List<int>();

        // 1. Direct document shares (where document is not owned by user and not deleted)
        var sharedDocQuery = _db.DocumentShares
            .AsNoTracking()
            .Where(s => s.SharedWithUserId == userId && s.Document.UserId != userId && !s.Document.IsDeleted);

        if (candidateDocumentIds != null)
        {
            var candidateList = candidateDocumentIds.Distinct().ToList();
            sharedDocQuery = sharedDocQuery.Where(s => candidateList.Contains(s.DocumentId));
        }

        var sharedDocIds = await sharedDocQuery.Select(s => s.DocumentId).ToListAsync();

        // 2. Inherited folder shares
        var sharedFolderIds = await _db.FolderShares
            .AsNoTracking()
            .Where(fs => fs.SharedWithUserId == userId && fs.Folder.UserId != userId && !fs.Folder.IsDeleted)
            .Select(fs => fs.FolderId)
            .ToListAsync();

        var inheritedDocIds = new List<int>();
        if (sharedFolderIds.Any())
        {
            var allFolderIds = new HashSet<int>(sharedFolderIds);
            var queue = new Queue<int>(sharedFolderIds);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                var children = await _db.Folders
                    .AsNoTracking()
                    .Where(f => f.ParentFolderId == current && !f.IsDeleted)
                    .Select(f => f.FolderId)
                    .ToListAsync();
                foreach (var child in children)
                {
                    if (allFolderIds.Add(child))
                    {
                        queue.Enqueue(child);
                    }
                }
            }

            var inheritedQuery = _db.Documents
                .AsNoTracking()
                .Where(d => !d.IsDeleted && d.UserId != userId && d.FolderId.HasValue && allFolderIds.Contains(d.FolderId.Value));

            if (candidateDocumentIds != null)
            {
                var candidateList = candidateDocumentIds.Distinct().ToList();
                inheritedQuery = inheritedQuery.Where(d => candidateList.Contains(d.DocumentId));
            }

            inheritedDocIds = await inheritedQuery.Select(d => d.DocumentId).ToListAsync();
        }

        return sharedDocIds.Concat(inheritedDocIds).Distinct().ToList();
    }

    public async Task<List<int>> GetViewableDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null)
    {
        if (candidateDocumentIds != null)
        {
            var list = candidateDocumentIds.Distinct().ToList();
            var accessible = new List<int>();
            foreach (var docId in list)
            {
                if (await CanViewDocumentAsync(docId, userId))
                {
                    accessible.Add(docId);
                }
            }
            return accessible;
        }

        if (userId <= 0)
        {
            return await _db.Documents
                .AsNoTracking()
                .Where(d => !d.IsDeleted && (d.SharingPermission == "PUBLIC" || d.GeneralAccess == "PUBLIC") && d.IsFlagged != true)
                .Select(d => d.DocumentId)
                .ToListAsync();
        }

        // Direct owned or public (unflagged)
        var directIds = await _db.Documents
            .AsNoTracking()
            .Where(d => !d.IsDeleted && (d.UserId == userId || ((d.SharingPermission == "PUBLIC" || d.GeneralAccess == "PUBLIC") && d.IsFlagged != true)))
            .Select(d => d.DocumentId)
            .ToListAsync();

        var sharedDocIds = await GetSharedDocumentIdsAsync(userId);

        return directIds.Concat(sharedDocIds).Distinct().ToList();
    }

    public Task<List<int>> GetAccessibleDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null) =>
        GetViewableDocumentIdsAsync(userId, candidateDocumentIds);

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
            ModerationStatus = doc.ModerationStatus,
            RequestedVisibility = doc.RequestedVisibility,
            SharingPermission = doc.SharingPermission,
            ModerationNote = doc.ModerationNote,
            ModerationSubmittedAt = doc.ModerationSubmittedAt,
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
        if (doc == null) throw new KeyNotFoundException("Tài liệu không tồn tại");
        if (doc.UserId != currentUserId) throw new UnauthorizedAccessException("Chỉ chủ sở hữu tài liệu mới có quyền thay đổi cài đặt truy cập");

        var user = await _db.Users.FindAsync(currentUserId);
        if (user == null) throw new KeyNotFoundException("Người dùng không tồn tại");

        string oldAccess = doc.GeneralAccess;
        string normalizedAccess = (generalAccess ?? "RESTRICTED").ToUpper().Trim();

        if (normalizedAccess == "PUBLIC")
        {
            if ("SUSPENDED".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Tài khoản đang bị tạm khóa/hạn chế, không thể yêu cầu công khai tài liệu.");

            doc.GeneralAccess = "PUBLIC";
            doc.RequestedVisibility = "PUBLIC";

            if (doc.ModerationStatus == "APPROVED")
            {
                doc.SharingPermission = "PUBLIC";
            }
            else
            {
                doc.SharingPermission = "PRIVATE";
                doc.ModerationStatus = "PENDING_REVIEW";
                doc.ModerationSubmittedAt = DateTime.Now;
                doc.ModerationNote = null;

                // Gửi thông báo cho toàn bộ Moderator đang hoạt động
                var moderators = await _db.Users
                    .Where(u => u.Role == "MODERATOR" && u.Status == "ACTIVE")
                    .Select(u => u.UserId)
                    .ToListAsync();

                _db.ModerationNotices.AddRange(moderators.Select(modId => new ModerationNotice
                {
                    UserId = modId,
                    DocumentId = doc.DocumentId,
                    Type = "DOCUMENT_REVIEW_PENDING",
                    Title = "Tài liệu mới cần xét duyệt",
                    Message = $"Tài liệu “{doc.Title}” vừa yêu cầu công khai và đang chờ xét duyệt.",
                    ActionUrl = $"/moderator?tab=queue&documentId={doc.DocumentId}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                }));
            }
        }
        else
        {
            doc.GeneralAccess = normalizedAccess == "LINK" ? "LINK" : "RESTRICTED";
            doc.SharingPermission = "PRIVATE";
            if (doc.ModerationStatus == "PENDING_REVIEW")
            {
                doc.RequestedVisibility = "PRIVATE";
                doc.ModerationStatus = "NOT_REQUESTED";
                doc.ModerationSubmittedAt = null;
            }
        }

        await _db.SaveChangesAsync();
        await LogAuditAsync(currentUserId, "GENERAL_ACCESS_CHANGED", "DOCUMENT", documentId, $"Changed from {oldAccess} to {doc.GeneralAccess} (ModerationStatus: {doc.ModerationStatus})");
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
        bool isNewShare = existingShare == null;
        DateTime sharedAt = DateTime.Now;
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
                CreatedAt = sharedAt
            });
            await LogAuditAsync(currentUserId, "SHARE_ADDED", "DOCUMENT", documentId, $"Shared with {email} as {role}");
        }
        await _db.SaveChangesAsync();

        if (isNewShare && _mailService != null && !string.IsNullOrWhiteSpace(targetUser.Email))
        {
            try
            {
                string? frontendBaseUrl = _configuration?["Frontend:BaseUrl"];
                var owner = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == currentUserId);

                if (Uri.TryCreate(frontendBaseUrl, UriKind.Absolute, out var baseUri) && owner != null)
                {
                    string documentUrl = new Uri(baseUri, $"/document/{documentId}").ToString();
                    bool emailSent = await _mailService.SendDocumentSharedNotificationAsync(
                        targetUser.Email,
                        targetUser.Username,
                        owner.Username,
                        doc.Title,
                        role,
                        sharedAt,
                        documentUrl);

                    if (!emailSent)
                    {
                        _logger?.LogWarning(
                            "Document {DocumentId} was shared with user {TargetUserId}, but the email notification failed",
                            documentId,
                            targetUser.UserId);
                    }
                }
                else
                {
                    _logger?.LogWarning(
                        "Document {DocumentId} was shared with user {TargetUserId}, but the email notification was skipped because Frontend:BaseUrl or owner information is invalid",
                        documentId,
                        targetUser.UserId);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(
                    ex,
                    "Document {DocumentId} was shared with user {TargetUserId}, but the email notification raised an exception",
                    documentId,
                    targetUser.UserId);
            }
        }
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
