using AIStudyHub.Application.Interfaces;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Infrastructure.Services.Gemini;

public class MockGeminiService : IGeminiService
{
    public Task<string> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory)
    {
        var lastUserMessage = messageHistory
            .LastOrDefault(m => m.Sender.Equals("USER", StringComparison.OrdinalIgnoreCase))?
            .MessageContent ?? "Hello";

        string mockResponse;
        if (lastUserMessage.Contains("polymorphism", StringComparison.OrdinalIgnoreCase) || lastUserMessage.Contains("đa hình", StringComparison.OrdinalIgnoreCase))
        {
            mockResponse = "[MOCK] Đa hình (Polymorphism) là một trong bốn tính chất cốt lõi của lập trình hướng đối tượng (OOP). Nó cho phép các đối tượng thuộc các lớp khác nhau phản hồi cùng một thông điệp (hàm gọi) theo cách riêng của chúng.";
        }
        else if (lastUserMessage.Contains("error", StringComparison.OrdinalIgnoreCase))
        {
            mockResponse = "[MOCK] Lỗi hệ thống giả lập hoặc phản hồi lỗi mẫu từ MockGeminiService.";
        }
        else
        {
            mockResponse = $"[MOCK] Trợ lý học tập AI Study Hub nhận được câu hỏi: \"{lastUserMessage}\". Đây là nội dung phản hồi mẫu.";
        }

        return Task.FromResult(mockResponse);
    }
}
