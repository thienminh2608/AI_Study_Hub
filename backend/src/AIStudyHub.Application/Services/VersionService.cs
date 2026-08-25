using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Application.Services;

public class VersionService : IVersionService
{
    private readonly IStudyHubDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly IPermissionService _permissionService;
    private readonly IDocumentProcessingQueue _queue;

    public VersionService(IStudyHubDbContext db, IFileStorage fileStorage, IPermissionService permissionService, IDocumentProcessingQueue queue)
    {
        _db = db;
        _fileStorage = fileStorage;
        _permissionService = permissionService;
        _queue = queue;
    }

    public async Task<DocumentVersionDto> CreateNewVersionAsync(int documentId, Stream fileStream, string fileName, string? changeSummary, int userId)
    {
        var doc = await _db.Documents
            .Include(d => d.DocumentVersions)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);

        if (doc == null || doc.IsDeleted) throw new KeyNotFoundException("Tài liệu không tồn tại");

        var effectiveRole = await _permissionService.GetEffectiveDocumentRoleAsync(documentId, userId);
        if (effectiveRole != "OWNER" && effectiveRole != "EDITOR")
        {
            throw new UnauthorizedAccessException("Chỉ chủ sở hữu hoặc người chỉnh sửa mới có quyền tải lên phiên bản mới");
        }

        string fileExtension = Path.GetExtension(fileName).TrimStart('.').ToLower();
        FileSecurityValidator.ValidateFile(fileStream, fileExtension);

        // Save current document file as a version if version history is empty
        if (!doc.DocumentVersions.Any())
        {
            var initialVersion = new DocumentVersion
            {
                DocumentId = doc.DocumentId,
                VersionNumber = 1,
                CloudStorageUrl = doc.CloudStorageUrl,
                FileExtension = doc.FileExtension,
                FileSizeMb = doc.FileSizeMb,
                ChangeSummary = "Phiên bản gốc",
                CreatedByUserId = doc.UserId,
                CreatedAt = doc.CreatedAt ?? DateTime.UtcNow,
                AiParsingStatus = doc.AiParsingStatus ?? "PENDING"
            };
            _db.DocumentVersions.Add(initialVersion);
            await _db.SaveChangesAsync();

            doc.CurrentVersionId = initialVersion.VersionId;
            await _db.SaveChangesAsync();
        }

        int maxVer = await _db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .MaxAsync(v => (int?)v.VersionNumber) ?? 0;
        int nextVersionNumber = maxVer + 1;
        decimal fileSizeMb = Math.Round((decimal)fileStream.Length / (1024 * 1024), 2);
        if (fileSizeMb == 0m) fileSizeMb = 0.01m;

        string relativePath = $"uploads/{userId}/v_{Guid.NewGuid():N}_{fileName}";
        await _fileStorage.SaveFileAsync(relativePath, fileStream);
        string cloudUrl = $"/{relativePath}";

        var newVersion = new DocumentVersion
        {
            DocumentId = doc.DocumentId,
            VersionNumber = nextVersionNumber,
            CloudStorageUrl = cloudUrl,
            FileExtension = fileExtension,
            FileSizeMb = fileSizeMb,
            ChangeSummary = changeSummary ?? $"Cập nhật phiên bản {nextVersionNumber}",
            CreatedByUserId = userId,
            CreatedAt = DateTime.UtcNow,
            AiParsingStatus = "QUEUED"
        };
        _db.DocumentVersions.Add(newVersion);
        await _db.SaveChangesAsync();

        // Update document current file properties
        doc.CloudStorageUrl = cloudUrl;
        doc.FileExtension = fileExtension;
        doc.FileSizeMb = fileSizeMb;
        doc.CurrentVersionId = newVersion.VersionId;
        doc.AiParsingStatus = "QUEUED";
        doc.UpdatedAt = DateTime.UtcNow;

        // Nếu tài liệu đang ở chế độ Public hoặc đã yêu cầu Public, phiên bản mới phải qua kiểm duyệt lại
        if (doc.GeneralAccess == "PUBLIC" || doc.SharingPermission == "PUBLIC")
        {
            doc.SharingPermission = "PRIVATE";
            doc.ModerationStatus = "PENDING_REVIEW";
            doc.ModerationSubmittedAt = DateTime.UtcNow;
            doc.ModerationNote = null;

            var moderators = await _db.Users
                .Where(u => u.Role == "MODERATOR" && u.Status == "ACTIVE")
                .Select(u => u.UserId)
                .ToListAsync();

            _db.ModerationNotices.AddRange(moderators.Select(modId => new ModerationNotice
            {
                UserId = modId,
                DocumentId = doc.DocumentId,
                Type = "DOCUMENT_REVIEW_PENDING",
                Title = "Tài liệu cập nhật phiên bản mới cần xét duyệt",
                Message = $"Tài liệu “{doc.Title}” vừa tải lên phiên bản mới (v{nextVersionNumber}) và đang chờ xét duyệt để công khai.",
                ActionUrl = $"/moderator?tab=queue&documentId={doc.DocumentId}",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            }));
        }

        await _db.SaveChangesAsync();

        // Enqueue background processing job for new version
        await _queue.EnqueueJobAsync(doc.DocumentId, newVersion.VersionId);

        await _permissionService.LogAuditAsync(userId, "VERSION_CREATED", "DOCUMENT", documentId, $"Created version {nextVersionNumber}");

        var user = await _db.Users.FindAsync(userId);

        return new DocumentVersionDto
        {
            VersionId = newVersion.VersionId,
            DocumentId = doc.DocumentId,
            VersionNumber = newVersion.VersionNumber,
            CloudStorageUrl = newVersion.CloudStorageUrl,
            FileExtension = newVersion.FileExtension,
            FileSizeMb = newVersion.FileSizeMb,
            ChangeSummary = newVersion.ChangeSummary,
            CreatedByUserId = userId,
            CreatedByName = user?.Username ?? "",
            CreatedAt = newVersion.CreatedAt,
            IsCurrent = true,
            AiParsingStatus = newVersion.AiParsingStatus
        };
    }

    public async Task<List<DocumentVersionDto>> GetVersionHistoryAsync(int documentId, int userId)
    {
        var effectiveRole = await _permissionService.GetEffectiveDocumentRoleAsync(documentId, userId);
        if (effectiveRole == "NONE") throw new UnauthorizedAccessException("Access denied");

        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null || doc.IsDeleted) throw new KeyNotFoundException("Document not found");

        var versions = await _db.DocumentVersions
            .AsNoTracking()
            .Include(v => v.CreatedByUser)
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => new DocumentVersionDto
            {
                VersionId = v.VersionId,
                DocumentId = v.DocumentId,
                VersionNumber = v.VersionNumber,
                CloudStorageUrl = v.CloudStorageUrl,
                FileExtension = v.FileExtension,
                FileSizeMb = v.FileSizeMb,
                ChangeSummary = v.ChangeSummary,
                CreatedByUserId = v.CreatedByUserId,
                CreatedByName = v.CreatedByUser.Username,
                CreatedAt = v.CreatedAt,
                IsCurrent = doc.CurrentVersionId.HasValue ? doc.CurrentVersionId.Value == v.VersionId : (v.CloudStorageUrl == doc.CloudStorageUrl),
                AiParsingStatus = v.AiParsingStatus
            })
            .ToListAsync();

        return versions;
    }

    public async Task RestoreVersionAsync(int documentId, int versionId, int userId)
    {
        var effectiveRole = await _permissionService.GetEffectiveDocumentRoleAsync(documentId, userId);
        if (effectiveRole != "OWNER" && effectiveRole != "EDITOR")
        {
            throw new UnauthorizedAccessException("Only owner or editor can restore version");
        }

        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null || doc.IsDeleted) throw new KeyNotFoundException("Document not found");

        var version = await _db.DocumentVersions.FirstOrDefaultAsync(v => v.VersionId == versionId && v.DocumentId == documentId);
        if (version == null) throw new KeyNotFoundException("Version not found");

        doc.CloudStorageUrl = version.CloudStorageUrl;
        doc.FileExtension = version.FileExtension;
        doc.FileSizeMb = version.FileSizeMb;
        doc.CurrentVersionId = version.VersionId;
        doc.AiParsingStatus = version.AiParsingStatus;
        doc.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        if (!string.Equals(version.AiParsingStatus, "READY", StringComparison.OrdinalIgnoreCase))
        {
            version.AiParsingStatus = "QUEUED";
            doc.AiParsingStatus = "QUEUED";
            await _db.SaveChangesAsync();
            await _queue.EnqueueJobAsync(doc.DocumentId, version.VersionId);
        }
        await _permissionService.LogAuditAsync(userId, "VERSION_RESTORED", "DOCUMENT", documentId, $"Restored to version {version.VersionNumber}");
    }

    public async Task DeleteVersionAsync(int documentId, int versionId, int userId)
    {
        var effectiveRole = await _permissionService.GetEffectiveDocumentRoleAsync(documentId, userId);
        if (effectiveRole != "OWNER" && effectiveRole != "EDITOR")
        {
            throw new UnauthorizedAccessException("Chỉ chủ sở hữu hoặc người chỉnh sửa mới có quyền xóa phiên bản");
        }

        var doc = await _db.Documents.Include(d => d.DocumentVersions).FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null || doc.IsDeleted) throw new KeyNotFoundException("Tài liệu không tồn tại");

        var version = await _db.DocumentVersions.FirstOrDefaultAsync(v => v.VersionId == versionId && v.DocumentId == documentId);
        if (version == null) throw new KeyNotFoundException("Phiên bản không tồn tại");

        var totalVersions = await _db.DocumentVersions.CountAsync(v => v.DocumentId == documentId);
        if (totalVersions <= 1)
        {
            throw new InvalidOperationException("Không thể xóa phiên bản duy nhất của tài liệu. Hãy tải lên phiên bản mới hoặc xóa toàn bộ tài liệu.");
        }

        bool isCurrent = doc.CurrentVersionId.HasValue
            ? doc.CurrentVersionId.Value == version.VersionId
            : (doc.CloudStorageUrl == version.CloudStorageUrl);
        if (isCurrent)
        {
            throw new InvalidOperationException("Không thể xóa phiên bản đang hoạt động. Vui lòng khôi phục sang một phiên bản khác trước khi xóa phiên bản này.");
        }

        // Guard against deleting version referenced by ANY DocumentReport
        bool hasReportEvidence = await _db.DocumentReports.AnyAsync(r => r.ReportedVersionId == versionId);
        if (hasReportEvidence)
        {
            throw new InvalidOperationException("Không thể xóa phiên bản này vì đang được lưu giữ làm bằng chứng cho báo cáo vi phạm.");
        }

        if (!string.IsNullOrWhiteSpace(version.CloudStorageUrl) && version.CloudStorageUrl != doc.CloudStorageUrl)
        {
            try
            {
                _fileStorage.DeleteFile(version.CloudStorageUrl.TrimStart('/'));
            }
            catch
            {
                // ignore
            }
        }

        _db.DocumentVersions.Remove(version);
        await _db.SaveChangesAsync();

        await _permissionService.LogAuditAsync(userId, "VERSION_DELETED", "DOCUMENT", documentId, $"Deleted version {version.VersionNumber}");
    }
}
