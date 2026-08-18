using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Application.Services;

public class TrashService : ITrashService
{
    private readonly IStudyHubDbContext _db;
    private readonly IFileStorage _fileStorage;
    private readonly IPermissionService _permissionService;

    public TrashService(IStudyHubDbContext db, IFileStorage fileStorage, IPermissionService permissionService)
    {
        _db = db;
        _fileStorage = fileStorage;
        _permissionService = permissionService;
    }

    public async Task MoveDocumentToTrashAsync(int documentId, int userId)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null || doc.IsDeleted) return;
        if (doc.UserId != userId) throw new UnauthorizedAccessException("Only document owner can move item to trash");

        doc.IsDeleted = true;
        doc.DeletedAt = DateTime.Now;
        doc.DeletedByUserId = userId;
        doc.LifeCycleStatus = "TRASHED";
        await _db.SaveChangesAsync();

        await _permissionService.LogAuditAsync(userId, "ITEM_TRASHED", "DOCUMENT", documentId, $"Moved document '{doc.Title}' to trash");
    }

    public async Task MoveFolderToTrashAsync(int folderId, int userId)
    {
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.FolderId == folderId);
        if (folder == null || folder.IsDeleted) return;
        if (folder.UserId != userId) throw new UnauthorizedAccessException("Only folder owner can move folder to trash");

        folder.IsDeleted = true;
        folder.DeletedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        await _permissionService.LogAuditAsync(userId, "ITEM_TRASHED", "FOLDER", folderId, $"Moved folder '{folder.FolderName}' to trash");
    }

    public async Task RestoreDocumentAsync(int documentId, int userId)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null || !doc.IsDeleted) return;
        if (doc.UserId != userId) throw new UnauthorizedAccessException("Only document owner can restore item");

        doc.IsDeleted = false;
        doc.DeletedAt = null;
        doc.DeletedByUserId = null;
        doc.LifeCycleStatus = "PRIVATE";
        await _db.SaveChangesAsync();

        await _permissionService.LogAuditAsync(userId, "ITEM_RESTORED", "DOCUMENT", documentId, $"Restored document '{doc.Title}'");
    }

    public async Task RestoreFolderAsync(int folderId, int userId)
    {
        var folder = await _db.Folders.FirstOrDefaultAsync(f => f.FolderId == folderId);
        if (folder == null || !folder.IsDeleted) return;
        if (folder.UserId != userId) throw new UnauthorizedAccessException("Only folder owner can restore folder");

        folder.IsDeleted = false;
        folder.DeletedAt = null;
        await _db.SaveChangesAsync();

        await _permissionService.LogAuditAsync(userId, "ITEM_RESTORED", "FOLDER", folderId, $"Restored folder '{folder.FolderName}'");
    }

    public async Task PermanentlyDeleteDocumentAsync(int documentId, int userId)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == documentId);
        if (doc == null) return;
        if (doc.UserId != userId) throw new UnauthorizedAccessException("Only document owner can permanently delete item");

        if (!string.IsNullOrWhiteSpace(doc.CloudStorageUrl))
        {
            _fileStorage.DeleteFile(doc.CloudStorageUrl.TrimStart('/'));
        }

        _db.Documents.Remove(doc);
        await _db.SaveChangesAsync();

        await _permissionService.LogAuditAsync(userId, "ITEM_PERMANENTLY_DELETED", "DOCUMENT", documentId, $"Permanently deleted document '{doc.Title}'");
    }

    public async Task PermanentlyDeleteFolderAsync(int folderId, int userId)
    {
        var folder = await _db.Folders
            .Include(f => f.Documents)
            .FirstOrDefaultAsync(f => f.FolderId == folderId);
        if (folder == null) return;
        if (folder.UserId != userId) throw new UnauthorizedAccessException("Only folder owner can permanently delete folder");

        foreach (var doc in folder.Documents.ToList())
        {
            if (!string.IsNullOrWhiteSpace(doc.CloudStorageUrl))
            {
                _fileStorage.DeleteFile(doc.CloudStorageUrl.TrimStart('/'));
            }
            _db.Documents.Remove(doc);
        }

        _db.Folders.Remove(folder);
        await _db.SaveChangesAsync();

        await _permissionService.LogAuditAsync(userId, "ITEM_PERMANENTLY_DELETED", "FOLDER", folderId, $"Permanently deleted folder '{folder.FolderName}'");
    }

    public async Task EmptyTrashAsync(int userId)
    {
        var docs = await _db.Documents.Where(d => d.UserId == userId && d.IsDeleted).ToListAsync();
        foreach (var doc in docs)
        {
            if (!string.IsNullOrWhiteSpace(doc.CloudStorageUrl))
            {
                _fileStorage.DeleteFile(doc.CloudStorageUrl.TrimStart('/'));
            }
            _db.Documents.Remove(doc);
        }

        var folders = await _db.Folders.Where(f => f.UserId == userId && f.IsDeleted).ToListAsync();
        foreach (var f in folders)
        {
            _db.Folders.Remove(f);
        }

        await _db.SaveChangesAsync();
        await _permissionService.LogAuditAsync(userId, "TRASH_EMPTIED", "USER", userId, "Emptied trash");
    }

    public async Task<PagedResult<TrashItemDto>> GetTrashItemsAsync(int userId, int pageNumber, int pageSize)
    {
        var docs = await _db.Documents
            .AsNoTracking()
            .Where(d => d.UserId == userId && d.IsDeleted)
            .Select(d => new TrashItemDto
            {
                ItemId = d.DocumentId,
                ItemType = "DOCUMENT",
                Name = d.Title,
                FileExtension = d.FileExtension,
                FileSizeMb = d.FileSizeMb,
                DeletedAt = d.DeletedAt ?? DateTime.UtcNow,
                DeletedByUserId = userId,
                DeletedByName = d.User.Username
            })
            .ToListAsync();

        var folders = await _db.Folders
            .AsNoTracking()
            .Where(f => f.UserId == userId && f.IsDeleted)
            .Select(f => new TrashItemDto
            {
                ItemId = f.FolderId,
                ItemType = "FOLDER",
                Name = f.FolderName,
                DeletedAt = f.DeletedAt ?? DateTime.UtcNow,
                DeletedByUserId = userId,
                DeletedByName = f.User.Username
            })
            .ToListAsync();

        var combined = docs.Concat(folders).OrderByDescending(x => x.DeletedAt).ToList();
        int totalCount = combined.Count;
        var paged = combined.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToList();

        return new PagedResult<TrashItemDto>(paged, totalCount, pageNumber, pageSize);
    }
}
