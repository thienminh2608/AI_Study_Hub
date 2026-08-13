using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace AIStudyHub.Application.Interfaces;

public interface IFolderService
{
    Task<List<FolderDto>> GetAllUserFoldersAsync(int userId);
    Task<List<FolderDto>> GetChildFoldersAsync(int userId, int? parentFolderId);
    Task<FolderDto?> GetFolderByIdAsync(int folderId);
    Task<bool> CreateFolderAsync(int userId, CreateFolderDto dto);
    Task<bool> UpdateFolderAsync(int userId, int folderId, UpdateFolderDto dto);
    Task<bool> DeleteFolderRecursiveAsync(int userId, int folderId);
    Task<bool> CheckDuplicateFolderNameAsync(int userId, string folderName, int? parentFolderId);
}

public class FolderDto
{
    public int FolderId
    {
        get; set;
    }
    public int UserId
    {
        get; set;
    }
    public int? ParentFolderId
    {
        get; set;
    }
    public string FolderName { get; set; } = null!;
    public string SharingPermission { get; set; } = null!;
    public DateTime? CreatedAt
    {
        get; set;
    }
}

public class CreateFolderDto
{
    public string FolderName { get; set; } = null!;
    public int? ParentFolderId
    {
        get; set;
    }
}

public class UpdateFolderDto
{
    public string FolderName { get; set; } = null!;
    public int? ParentFolderId
    {
        get; set;
    }
    public string SharingPermission { get; set; } = null!;
}
