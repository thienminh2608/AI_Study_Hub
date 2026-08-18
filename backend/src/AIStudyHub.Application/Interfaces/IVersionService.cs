using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IVersionService
{
    Task<DocumentVersionDto> CreateNewVersionAsync(int documentId, Stream fileStream, string fileName, string? changeSummary, int userId);
    Task<List<DocumentVersionDto>> GetVersionHistoryAsync(int documentId, int userId);
    Task RestoreVersionAsync(int documentId, int versionId, int userId);
}
