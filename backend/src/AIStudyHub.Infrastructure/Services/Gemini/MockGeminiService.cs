using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;

namespace AIStudyHub.Infrastructure.Services.Gemini;

public partial class MockGeminiService : IGeminiService
{
    public Task<string> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory, CancellationToken cancellationToken = default)
    {
        var lastMessage = messageHistory.LastOrDefault()?.MessageContent ?? "";

        if (lastMessage.StartsWith("Document:", StringComparison.OrdinalIgnoreCase) && lastMessage.Contains("[CHUNK id="))
        {
            var match = ChunkMarkerRegex().Match(lastMessage);
            var chunkId = match.Success ? match.Groups[1].Value : "1";
            var page = match.Success && match.Groups[2].Value != "null" ? match.Groups[2].Value : "null";
            var pageJson = page == "null" ? "null" : page;
            return Task.FromResult(
                $"{{\"answer\":\"[MOCK] Đây là câu trả lời mẫu dựa trên nội dung tài liệu đã đọc.\",\"citations\":[{{\"chunkId\":{chunkId},\"page\":{pageJson}}}],\"insufficientContext\":false}}");
        }

        var viewMatch = ViewCommandRegex().Match(lastMessage);
        if (viewMatch.Success)
        {
            return Task.FromResult($"VIEW/{viewMatch.Groups[1].Value}");
        }

        var lastUserMessage = messageHistory
            .LastOrDefault(m => m.Sender.Equals("USER", StringComparison.OrdinalIgnoreCase))?
            .MessageContent ?? "Hello";

        string mockResponse;
        if (lastUserMessage.Contains("polymorphism", StringComparison.OrdinalIgnoreCase) || lastUserMessage.Contains("đa hình", StringComparison.OrdinalIgnoreCase))
        {
            mockResponse = "RESPONSE: [MOCK] Đa hình (Polymorphism) là một trong bốn tính chất cốt lõi của lập trình hướng đối tượng (OOP). Nó cho phép các đối tượng thuộc các lớp khác nhau phản hồi cùng một thông điệp (hàm gọi) theo cách riêng của chúng.";
        }
        else if (lastUserMessage.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            mockResponse = "RESPONSE: [MOCK] Lỗi hệ thống giả lập hoặc phản hồi lỗi mẫu từ MockGeminiService.";
        }
        else
        {
            mockResponse = $"RESPONSE: [MOCK] Trợ lý học tập AI Study Hub nhận được câu hỏi: \"{lastUserMessage}\". Đây là nội dung phản hồi mẫu.";
        }

        return Task.FromResult(mockResponse);
    }

    [GeneratedRegex(@"\[CHUNK id=(\d+)[^\]]*page=(\d+|null)")]
    private static partial Regex ChunkMarkerRegex();

    [GeneratedRegex(@"VIEW/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ViewCommandRegex();
}
