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
    public Task<GeminiResponseDto> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory, string operation = "CHAT", CancellationToken cancellationToken = default)
    {
        var lastMessage = messageHistory.LastOrDefault()?.MessageContent ?? "";

        if (lastMessage.StartsWith("Document:", StringComparison.OrdinalIgnoreCase) && lastMessage.Contains("[CHUNK id="))
        {
            var match = ChunkMarkerRegex().Match(lastMessage);
            var chunkId = match.Success ? match.Groups[1].Value : "1";
            var page = match.Success && match.Groups[2].Value != "null" ? match.Groups[2].Value : "null";
            var pageJson = page == "null" ? "null" : page;
            string content = $"{{\"answer\":\"[MOCK] Đây là câu trả lời mẫu dựa trên nội dung tài liệu đã đọc.\",\"citations\":[{{\"chunkId\":{chunkId},\"page\":{pageJson}}}],\"insufficientContext\":false}}";
            return Task.FromResult(CreateMockResult(content, operation, 250, 80));
        }

        var viewMatch = ViewCommandRegex().Match(lastMessage);
        if (viewMatch.Success)
        {
            return Task.FromResult(CreateMockResult($"VIEW/{viewMatch.Groups[1].Value}", operation, 100, 10));
        }

        var lastUserMessage = messageHistory
            .LastOrDefault(m => m.Sender.Equals("USER", StringComparison.OrdinalIgnoreCase))?
            .MessageContent ?? "Hello";

        string mockResponse;
        if (lastUserMessage.Contains("Hãy dùng SEARCH", StringComparison.OrdinalIgnoreCase))
        {
            mockResponse = "SEARCH";
        }
        else if (lastUserMessage.Contains("polymorphism", StringComparison.OrdinalIgnoreCase) || lastUserMessage.Contains("đa hình", StringComparison.OrdinalIgnoreCase))
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

        return Task.FromResult(CreateMockResult(mockResponse, operation, 180, 60));
    }

    private static GeminiResponseDto CreateMockResult(string content, string operation, int promptTokens, int completionTokens)
    {
        return new GeminiResponseDto
        {
            Content = content,
            Provider = "MockGoogle",
            Model = "gemini-3.1-flash-lite-mock",
            Operation = operation,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
            CachedTokens = 0,
            TotalTokens = promptTokens + completionTokens,
            LatencyMs = 45,
            Status = "SUCCESS",
            EstimatedCost = Math.Round((promptTokens * 0.000000075m) + (completionTokens * 0.00000030m), 6),
            Currency = "USD",
            PricingVersion = "2026.1",
            RequestId = Guid.NewGuid().ToString("N")
        };
    }

    [GeneratedRegex(@"\[CHUNK id=(\d+)[^\]]*page=(\d+|null)")]
    private static partial Regex ChunkMarkerRegex();

    [GeneratedRegex(@"VIEW/(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ViewCommandRegex();

    public Task<string> ExtractTextFromImageAsync(byte[] imageBytes, string mimeType, CancellationToken cancellationToken = default)
    {
        return Task.FromResult("# Tài liệu hình ảnh (Trích xuất OCR)\n\nNội dung văn bản được trích xuất từ tệp hình ảnh bằng AI Study Hub Vision.\n- Môn học: Tài liệu học tập\n- Văn bản: Đầy đủ các công thức và kiến thức trích xuất từ ảnh.");
    }
}
