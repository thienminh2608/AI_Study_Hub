using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IGeminiService
{
    Task<GeminiResponseDto> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory, string operation = "CHAT", CancellationToken cancellationToken = default);
    Task<string> ExtractTextFromImageAsync(byte[] imageBytes, string mimeType, CancellationToken cancellationToken = default);
}
