using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IDocumentService
{
    Task<DocumentResponseDto> UploadDocumentAsync(int userId, int? folderId, string originalFileName, string fileExtension, long fileSizeInBytes, Stream fileStream);
    Task<DocumentResponseDto> ConfirmDocumentAsync(int userId, int documentId, string title, string subject, string sharingPermission, int? folderId);
    Task<DocumentResponseDto> ReplaceDocumentAsync(int userId, int pendingDocId, int duplicateDocId, string title, string subject, string sharingPermission, int? folderId);
    Task<DocumentResponseDto> KeepBothDocumentsAsync(int userId, int pendingDocId, string title, string subject, string sharingPermission, int? folderId);
    Task CancelUploadAsync(int userId, int pendingDocId);
    Task<List<DocumentResponseDto>> GetUserDocumentsAsync(int userId, int? folderId);
    Task<List<DocumentResponseDto>> GetPublicDocumentsAsync();
    Task<DocumentResponseDto?> GetDocumentByIdAsync(int documentId);
    Task<bool> DeleteDocumentAsync(int userId, int documentId);
    Task<string?> GetExtractedTextAsync(int documentId);
    Task<bool> CheckDuplicateTitleAsync(int userId, string title, string fileExtension, int? folderId, int? excludeDocId);
    Task<List<DocumentReportResponseDto>> GetReportsAsync();
    Task<bool> ReportDocumentAsync(int reporterId, DocumentReportDto reportDto);
    Task<List<PublicReportReasonDto>> GetReportReasonsAsync();
    Task<bool> ResolveReportAsync(int adminId, int reportId, string action);
    Task<List<int>> GetBookmarkedDocumentIdsAsync(int userId);
    Task<int?> AddBookmarkAsync(int userId, int documentId);
    Task<int?> RemoveBookmarkAsync(int userId, int documentId);
    Task<int?> IncrementDownloadCountAsync(int documentId, int userId);
    Task<int?> IncrementViewCountAsync(int documentId, int userId);
    Task<DocumentAnalyticsDto> GetUserAnalyticsAsync(int userId);
    Task<DocumentDetailDto?> GetDocumentDetailAsync(int documentId);

    // New Features & Background Processing Queue
    Task ProcessExtractionAsync(int documentId);
    Task RetryExtractionAsync(int documentId, int userId);
    Task<StorageQuotaDto> GetUserStorageQuotaAsync(int userId);
    Task<PagedResult<DocumentResponseDto>> GetMyDocumentsPagedAsync(int userId, int? folderId, int pageNumber, int pageSize, string? search, string? subject);
    Task<PagedResult<DocumentResponseDto>> GetSharedWithMePagedAsync(int userId, int pageNumber, int pageSize);
    Task<PagedResult<DocumentResponseDto>> GetBookmarksPagedAsync(int userId, int pageNumber, int pageSize);
    Task BulkDeleteDocumentsAsync(List<int> documentIds, int userId);
    Task BulkMoveDocumentsAsync(List<int> documentIds, int? targetFolderId, int userId);
}
