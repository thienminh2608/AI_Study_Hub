using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IPermissionService
{
    Task<string> GetEffectiveDocumentRoleAsync(int documentId, int userId, string? shareToken = null);
    Task<string> GetEffectiveFolderRoleAsync(int folderId, int userId);
    Task<bool> CanViewDocumentAsync(int documentId, int userId, string? shareToken = null);
    Task<bool> CanDownloadDocumentAsync(int documentId, int userId, string? shareToken = null);
    Task<bool> CanEditDocumentAsync(int documentId, int userId);
    Task<bool> CanManageDocumentAccessAsync(int documentId, int userId);
    Task<List<int>> GetSharedDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null);
    Task<List<int>> GetViewableDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null);
    Task<List<int>> GetAccessibleDocumentIdsAsync(int userId, IEnumerable<int>? candidateDocumentIds = null);
    Task<ItemAccessSettingsDto> GetDocumentAccessSettingsAsync(int documentId, int currentUserId);
    Task<ItemAccessSettingsDto> GetFolderAccessSettingsAsync(int folderId, int currentUserId);
    Task UpdateDocumentGeneralAccessAsync(int documentId, string generalAccess, int currentUserId);
    Task UpdateFolderGeneralAccessAsync(int folderId, string generalAccess, int currentUserId);
    Task AddOrUpdateDocumentUserShareAsync(int documentId, string email, string role, int currentUserId);
    Task AddOrUpdateFolderUserShareAsync(int folderId, string email, string role, int currentUserId);
    Task RemoveDocumentUserShareAsync(int documentId, int targetUserId, int currentUserId);
    Task RemoveFolderUserShareAsync(int folderId, int targetUserId, int currentUserId);
    Task<ShareLinkInfoDto> RotateDocumentShareLinkAsync(int documentId, int currentUserId);
    Task RevokeDocumentShareLinkAsync(int documentId, int currentUserId);
    Task LogAuditAsync(int actorUserId, string action, string targetType, int targetId, string? details);
    Task<PagedResult<AuditLogDto>> GetAuditLogsAsync(int pageNumber, int pageSize);
}
