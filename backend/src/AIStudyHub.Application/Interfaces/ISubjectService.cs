using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface ISubjectService
{
    Task<List<SubjectDto>> GetApprovedSubjectsAsync(CancellationToken cancellationToken = default);
    Task<List<SubjectTreeDto>> GetSubjectTreeAsync(string? status = "APPROVED", CancellationToken cancellationToken = default);
    Task<List<SubjectDto>> GetSubjectsForModeratorAsync(string? status, string? search, CancellationToken cancellationToken = default);
    Task<List<string>> GetDescendantSubjectNamesAsync(string subjectName, CancellationToken cancellationToken = default);
    Task<List<int>> GetDescendantSubjectIdsAsync(int subjectId, CancellationToken cancellationToken = default);
    Task<string> CreateOrResolveSubjectAsync(string subjectName, int userId, int? parentSubjectId = null, CancellationToken cancellationToken = default);
    Task<string> CreateOrResolveSubjectPathAsync(string subjectName, string? childSubjectName, int userId, CancellationToken cancellationToken = default);
    Task<SubjectDto> CreateSubjectAsync(string subjectName, int userId, int? parentSubjectId = null, int sortOrder = 0, bool autoApprove = false, CancellationToken cancellationToken = default);
    Task<SubjectDto> ApproveSubjectAsync(int subjectId, int moderatorId, CancellationToken cancellationToken = default);
    Task<SubjectDto> RejectSubjectAsync(int subjectId, string reason, int moderatorId, CancellationToken cancellationToken = default);
    Task<bool> MoveSubjectSubtreeAsync(int subjectId, int? newParentSubjectId, int newSortOrder, CancellationToken cancellationToken = default);
    Task DeleteSubjectAsync(int subjectId, int moderatorId, CancellationToken cancellationToken = default);
}
