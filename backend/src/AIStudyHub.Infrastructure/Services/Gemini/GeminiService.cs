using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
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

    public async Task<string> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory, GeminiRequestOptions? options = null, CancellationToken cancellationToken = default)
    {
        string apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("LỖI BẢO MẬT: Chưa cấu hình GEMINI_API_KEY trong biến môi trường hoặc file appsettings.json!");
        }

        string model = (_configuration["Gemini:Model"] ?? "gemini-3.1-flash-lite").Trim();
        if (string.IsNullOrWhiteSpace(model))
            throw new InvalidOperationException("Gemini:Model chưa được cấu hình.");
        string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

        var requestOptions = options ?? new GeminiRequestOptions();
        int maxOutputTokens = GetSetting("Gemini:MaxOutputTokens", requestOptions.MaxOutputTokens);
        int timeoutSeconds = GetSetting("Gemini:RequestTimeoutSeconds", requestOptions.TimeoutSeconds);
        int maxRetries = GetSetting("Gemini:RetryCount", requestOptions.MaxRetries);
        int retryBaseDelayMilliseconds = GetSetting("Gemini:RetryBaseDelayMilliseconds", requestOptions.RetryBaseDelayMilliseconds);

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

        var payload = new
        {
            contents = contentsList,
            generationConfig = new
            {
                maxOutputTokens
            }
        };
        string jsonInputString = JsonSerializer.Serialize(payload);

        Exception? lastError = null;
        for (int attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = new StringContent(jsonInputString, Encoding.UTF8, "application/json");

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutSeconds > 0)
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeoutCts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    string errorDetails = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                    var transient = (int)response.StatusCode >= 500 || (int)response.StatusCode == 429;
                    var httpError = new HttpRequestException($"Lỗi gọi Gemini API (HTTP {response.StatusCode}): {errorDetails}");
                    if (transient && attempt < maxRetries)
                    {
                        lastError = httpError;
                    }
                    else
                    {
                        throw httpError;
                    }
                }
                else
                {
                    string responseString = await response.Content.ReadAsStringAsync(timeoutCts.Token);
                    var jsonNode = JsonNode.Parse(responseString);

                    try
                    {
                        string aiResponse = jsonNode?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>() ?? "";
                        if (string.IsNullOrWhiteSpace(aiResponse))
                        {
                            var blockReason = jsonNode?["promptFeedback"]?["blockReason"]?.GetValue<string>();
                            throw new InvalidOperationException(string.IsNullOrWhiteSpace(blockReason)
                                ? "Gemini không trả về nội dung. Vui lòng thử lại."
                                : $"Gemini đã từ chối yêu cầu: {blockReason}.");
                        }

                        return aiResponse.Trim();
                    }
                    catch (Exception ex)
                    {
                        throw new JsonException("Lỗi định dạng response từ Gemini API", ex);
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxRetries)
            {
                lastError = new TimeoutException($"Gemini request timed out after {timeoutSeconds} seconds.");
            }
            catch (HttpRequestException ex) when (attempt < maxRetries)
            {
                lastError = ex;
            }

            if (attempt < maxRetries)
            {
                int delayMilliseconds = Math.Max(100, retryBaseDelayMilliseconds * (int)Math.Pow(2, attempt));
                await Task.Delay(delayMilliseconds, cancellationToken);
            }
        }

        throw lastError ?? new InvalidOperationException("Gemini không trả về nội dung. Vui lòng thử lại.");
    }

    private int GetSetting(string key, int defaultValue)
    {
        return _configuration.GetValue<int?>(key) ?? defaultValue;
    }
}
