using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IChatService
{
    Task<List<ChatSessionDto>> GetUserSessionsAsync(int userId);
    Task<ChatSessionDto> CreateSessionAsync(int userId, CreateSessionDto dto);
    Task<bool> PinSessionAsync(int userId, int sessionId, bool pin);
    Task<bool> DeleteSessionAsync(int userId, int sessionId);
    Task<ChatSessionDto?> SetAttachedDocumentAsync(int userId, int sessionId, int? documentId, int? versionId = null);
    Task<List<ChatMessageDto>> GetSessionMessagesAsync(int userId, int sessionId);
    Task<ChatAnswerDto> ProcessUserMessageAsync(int userId, int sessionId, AskQuestionDto dto, CancellationToken cancellationToken = default);
    Task<CitationResolveDto?> ResolveCitationAsync(int userId, long citationId, CancellationToken cancellationToken = default);
}
