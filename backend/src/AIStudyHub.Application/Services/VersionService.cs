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

    public VersionService(IStudyHubDbContext db, IFileStorage fileStorage, IPermissionService permissionService)
    {
        _db = db;
        _fileStorage = fileStorage;
        _permissionService = permissionService;
    }

    public async Task<DocumentVersionDto> CreateNewVersionAsync(int documentId, Stream fileStream, string fileName, string? changeSummary, int userId)
    {
        var doc = await _db.Documents
            .Include(d => d.DocumentVersions)
            .FirstOrDefaultAsync(d => d.DocumentId == documentId);

        if (doc == null || doc.IsDeleted) throw new KeyNotFoundException("Document not found");

        var effectiveRole = await _permissionService.GetEffectiveDocumentRoleAsync(documentId, userId);
        if (effectiveRole != "OWNER" && effectiveRole != "EDITOR")
        {
            throw new UnauthorizedAccessException("Only owner or editor can upload new version");
        }

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
                CreatedAt = doc.CreatedAt ?? DateTime.Now
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
        string fileExtension = Path.GetExtension(fileName).TrimStart('.').ToLower();
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
            CreatedAt = DateTime.Now
        };
        _db.DocumentVersions.Add(newVersion);
        await _db.SaveChangesAsync();

        // Update document current file properties
        doc.CloudStorageUrl = cloudUrl;
        doc.FileExtension = fileExtension;
        doc.FileSizeMb = fileSizeMb;
        doc.CurrentVersionId = newVersion.VersionId;
        doc.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();

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
            IsCurrent = true
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
                IsCurrent = doc.CurrentVersionId.HasValue ? doc.CurrentVersionId.Value == v.VersionId : (v.CloudStorageUrl == doc.CloudStorageUrl)
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
        doc.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync();
        await _permissionService.LogAuditAsync(userId, "VERSION_RESTORED", "DOCUMENT", documentId, $"Restored to version {version.VersionNumber}");
    }
}
