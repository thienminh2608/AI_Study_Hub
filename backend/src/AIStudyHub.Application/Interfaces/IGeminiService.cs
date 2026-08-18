using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IGeminiService
{
    Task<string> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory, GeminiRequestOptions? options = null, CancellationToken cancellationToken = default);
}
