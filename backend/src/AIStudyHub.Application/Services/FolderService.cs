using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Application.Services;

public class FolderService : IFolderService
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private const string UploadFolder = "uploads";

    public FolderService(IStudyHubDbContext dbContext, IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<List<FolderDto>> GetAllUserFoldersAsync(int userId)
    {
        var folders = await _dbContext.Folders
            .Where(f => f.UserId == userId && !f.IsDeleted)
            .OrderBy(f => f.FolderName)
            .ToListAsync();

        return folders.Select(MapToDto).ToList();
    }

    public async Task<List<FolderDto>> GetChildFoldersAsync(int userId, int? parentFolderId)
    {
        IQueryable<Folder> query;
        if (parentFolderId.HasValue)
        {
            query = _dbContext.Folders.Where(f => !f.IsDeleted && f.ParentFolderId == parentFolderId.Value);
        }
        else
        {
            var sharedFolderIds = await _dbContext.FolderShares
                .Where(fs => fs.SharedWithUserId == userId)
                .Select(fs => fs.FolderId)
                .ToListAsync();

            query = _dbContext.Folders.Where(f => !f.IsDeleted && f.ParentFolderId == null && (f.UserId == userId || sharedFolderIds.Contains(f.FolderId)));
        }

        var folders = await query.OrderBy(f => f.FolderName).ToListAsync();
        return folders.Select(MapToDto).ToList();
    }

    public async Task<FolderDto?> GetFolderByIdAsync(int folderId)
    {
        var folder = await _dbContext.Folders.FirstOrDefaultAsync(f => f.FolderId == folderId && !f.IsDeleted);
        if (folder == null)
            return null;
        return MapToDto(folder);
    }

    public async Task<bool> CreateFolderAsync(int userId, CreateFolderDto dto)
    {
        string cleanName = dto.FolderName.Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
            throw new ArgumentException("Tên thư mục không được để trống.");
        if (dto.ParentFolderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == dto.ParentFolderId && f.UserId == userId))
            throw new UnauthorizedAccessException("Thư mục cha không hợp lệ.");
        bool hasDuplicate = await CheckDuplicateFolderNameAsync(userId, cleanName, dto.ParentFolderId);
        if (hasDuplicate)
        {
            throw new InvalidOperationException("Tên thư mục đã tồn tại ở vị trí này.");
        }

        var folder = new Folder
        {
            UserId = userId,
            ParentFolderId = dto.ParentFolderId,
            FolderName = cleanName,
            SharingPermission = "PRIVATE",
            CreatedAt = DateTime.Now
        };

        _dbContext.Folders.Add(folder);
        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> UpdateFolderAsync(int userId, int folderId, UpdateFolderDto dto)
    {
        var folder = await _dbContext.Folders.FirstOrDefaultAsync(f => f.FolderId == folderId && f.UserId == userId);
        if (folder == null)
            return false;

        string cleanName = dto.FolderName.Trim();
        if (string.IsNullOrWhiteSpace(cleanName))
            throw new ArgumentException("Tên thư mục không được để trống.");
        if (dto.ParentFolderId.HasValue && !await _dbContext.Folders.AnyAsync(f => f.FolderId == dto.ParentFolderId && f.UserId == userId))
            throw new UnauthorizedAccessException("Thư mục cha không hợp lệ.");
        if (folder.FolderName != cleanName || folder.ParentFolderId != dto.ParentFolderId)
        {
            bool hasDuplicate = await CheckDuplicateFolderNameAsync(userId, cleanName, dto.ParentFolderId);
            if (hasDuplicate)
            {
                throw new InvalidOperationException("Tên thư mục đã tồn tại ở vị trí này.");
            }
        }

        // Prevent moving folder into itself or its own descendants
        if (dto.ParentFolderId.HasValue)
        {
            if (dto.ParentFolderId.Value == folderId)
            {
                throw new InvalidOperationException("Không thể di chuyển thư mục vào chính nó.");
            }

            var childIds = new List<int>();
            await GetDescendantFolderIdsAsync(folderId, childIds);
            if (childIds.Contains(dto.ParentFolderId.Value))
            {
                throw new InvalidOperationException("Không thể di chuyển thư mục cha vào thư mục con của nó.");
            }
        }

        folder.FolderName = cleanName;
        folder.ParentFolderId = dto.ParentFolderId;
        var permission = dto.SharingPermission?.Trim().ToUpperInvariant();
        if (permission is not ("PRIVATE" or "PUBLIC"))
            throw new ArgumentException("Quyền chia sẻ không hợp lệ.");
        folder.SharingPermission = permission;

        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> DeleteFolderRecursiveAsync(int userId, int folderId)
    {
        var folder = await _dbContext.Folders.FirstOrDefaultAsync(f => f.FolderId == folderId && f.UserId == userId);
        if (folder == null)
            return false;

        var allFolderIds = new List<int> { folderId };
        await GetDescendantFolderIdsAsync(folderId, allFolderIds);

        // Fetch all documents in these folders
        var docs = await _dbContext.Documents
            .Where(d => d.UserId == userId && d.FolderId.HasValue && allFolderIds.Contains(d.FolderId.Value))
            .ToListAsync();

        // Soft delete child documents
        foreach (var doc in docs)
        {
            doc.IsDeleted = true;
            doc.DeletedAt = DateTime.Now;
            doc.DeletedByUserId = userId;
            doc.LifeCycleStatus = "TRASHED";
        }

        // Soft delete folders
        foreach (var id in allFolderIds)
        {
            var f = await _dbContext.Folders.FindAsync(id);
            if (f != null)
            {
                f.IsDeleted = true;
                f.DeletedAt = DateTime.Now;
            }
        }

        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<bool> CheckDuplicateFolderNameAsync(int userId, string folderName, int? parentFolderId)
    {
        var query = _dbContext.Folders.Where(f => f.UserId == userId && f.FolderName == folderName);
        if (parentFolderId.HasValue)
        {
            query = query.Where(f => f.ParentFolderId == parentFolderId.Value);
        }
        else
        {
            query = query.Where(f => f.ParentFolderId == null);
        }

        return await query.AnyAsync();
    }

    private async Task GetDescendantFolderIdsAsync(int folderId, List<int> folderIds)
    {
        var children = await _dbContext.Folders
            .Where(f => f.ParentFolderId == folderId)
            .Select(f => f.FolderId)
            .ToListAsync();

        foreach (var childId in children)
        {
            if (!folderIds.Contains(childId))
            {
                folderIds.Add(childId);
                await GetDescendantFolderIdsAsync(childId, folderIds);
            }
        }
    }

    private FolderDto MapToDto(Folder f)
    {
        return new FolderDto
        {
            FolderId = f.FolderId,
            UserId = f.UserId,
            ParentFolderId = f.ParentFolderId,
            FolderName = f.FolderName,
            SharingPermission = f.SharingPermission ?? "PRIVATE",
            CreatedAt = f.CreatedAt
        };
    }
}
