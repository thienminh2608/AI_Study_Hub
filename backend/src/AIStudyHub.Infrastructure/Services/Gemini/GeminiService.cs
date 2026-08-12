using AIStudyHub.Application.Interfaces;

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using Microsoft.Extensions.Configuration;

namespace AIStudyHub.Infrastructure.Services.Gemini;

public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public GeminiService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    private string GetApiKey()
    {
        string apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            apiKey = _configuration["Gemini:ApiKey"] ?? "";
        }
        return apiKey.Trim();
    }

    public async Task<string> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory)
    {
        string apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("LỖI BẢO MẬT: Chưa cấu hình GEMINI_API_KEY trong biến môi trường hoặc file appsettings.json!");
        }

        string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-flash-lite:generateContent?key={apiKey}";

        // 1. Build Payload JSON using System.Text.Json
        var contentsList = new List<object>();

        foreach (var msg in messageHistory)
        {
            // Convert sender roles to Gemini specification (user, model)
            string role = "user";
            if (msg.Sender.Equals("BOT", StringComparison.OrdinalIgnoreCase))
            {
                role = "model";
            }

            contentsList.Add(new
            {
                role = role,
                parts = new[]
                {
                    new { text = msg.MessageContent }
                }
            });
        }

        var payload = new { contents = contentsList };
        string jsonInputString = JsonSerializer.Serialize(payload);

        // 2. Send request
        var content = new StringContent(jsonInputString, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync(apiUrl, content);

        if (!response.IsSuccessStatusCode)
        {
            string errorDetails = await response.Content.ReadAsStringAsync();
            throw new HttpRequestException($"Lỗi gọi Gemini API (HTTP {response.StatusCode}): {errorDetails}");
        }

        // 3. Read and Parse Response
        string responseString = await response.Content.ReadAsStringAsync();
        var jsonNode = JsonNode.Parse(responseString);
        
        try
        {
            string aiResponse = jsonNode?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>() ?? "";
            return aiResponse;
        }
        catch (Exception ex)
        {
            throw new JsonException("Lỗi định dạng response từ Gemini API", ex);
        }
    }
}
