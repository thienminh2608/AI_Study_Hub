using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface ITrashService
{
    Task MoveDocumentToTrashAsync(int documentId, int userId);
    Task MoveFolderToTrashAsync(int folderId, int userId);
    Task RestoreDocumentAsync(int documentId, int userId);
    Task RestoreFolderAsync(int folderId, int userId);
    Task PermanentlyDeleteDocumentAsync(int documentId, int userId);
    Task PermanentlyDeleteFolderAsync(int folderId, int userId);
    Task EmptyTrashAsync(int userId);
    Task<PagedResult<TrashItemDto>> GetTrashItemsAsync(int userId, int pageNumber, int pageSize);
}
