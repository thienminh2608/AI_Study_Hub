using System;
using System.Collections.Generic;
using System.Net;
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

    private const int DefaultMaxOutputTokens = 2048;
    private const int DefaultTimeoutSeconds = 30;
    private const int MaxRetryAttempts = 3;
    private const int BaseBackoffMilliseconds = 200;

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

    private int GetMaxOutputTokens()
    {
        return int.TryParse(_configuration["Gemini:MaxOutputTokens"], out var value) && value > 0
            ? value
            : DefaultMaxOutputTokens;
    }

    private int GetTimeoutSeconds()
    {
        return int.TryParse(_configuration["Gemini:TimeoutSeconds"], out var value) && value > 0
            ? value
            : DefaultTimeoutSeconds;
    }

    public async Task<string> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory, CancellationToken cancellationToken = default)
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

        var contentsList = new List<object>();

        foreach (var msg in messageHistory)
        {
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
                maxOutputTokens = GetMaxOutputTokens()
            }
        };
        string jsonInputString = JsonSerializer.Serialize(payload);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(GetTimeoutSeconds()));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        string responseString;
        int attempt = 0;
        while (true)
        {
            attempt++;
            HttpResponseMessage response;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Headers.Add("x-goog-api-key", apiKey);
                request.Content = new StringContent(jsonInputString, Encoding.UTF8, "application/json");
                response = await _httpClient.SendAsync(request, linkedCts.Token);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException) when (attempt < MaxRetryAttempts)
            {
                await Task.Delay(BaseBackoffMilliseconds * (int)Math.Pow(2, attempt - 1), cancellationToken);
                continue;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Gemini API không phản hồi sau {GetTimeoutSeconds()}s (đã thử {attempt} lần).");
            }
            catch (HttpRequestException) when (attempt < MaxRetryAttempts)
            {
                await Task.Delay(BaseBackoffMilliseconds * (int)Math.Pow(2, attempt - 1), cancellationToken);
                continue;
            }

            bool isTransientStatus = response.StatusCode == HttpStatusCode.TooManyRequests ||
                                      (int)response.StatusCode >= 500;

            if (!response.IsSuccessStatusCode)
            {
                if (isTransientStatus && attempt < MaxRetryAttempts)
                {
                    response.Dispose();
                    await Task.Delay(BaseBackoffMilliseconds * (int)Math.Pow(2, attempt - 1), cancellationToken);
                    continue;
                }

                string errorDetails = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Dispose();
                throw new HttpRequestException($"Lỗi gọi Gemini API (HTTP {response.StatusCode}): {errorDetails}");
            }

            responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            break;
        }

        var jsonNode = JsonNode.Parse(responseString);

        try
        {
            string aiResponse = jsonNode?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>() ?? "";
            if (string.IsNullOrWhiteSpace(aiResponse))
            {
                var blockReason = jsonNode?["promptFeedback"]?["blockReason"]?.GetValue<string>();
                var finishReason = jsonNode?["candidates"]?[0]?["finishReason"]?.GetValue<string>();
                if (!string.IsNullOrWhiteSpace(blockReason))
                    throw new InvalidOperationException($"Gemini đã từ chối yêu cầu: {blockReason}.");
                if ("MAX_TOKENS".Equals(finishReason, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Gemini đã đạt giới hạn output token trước khi hoàn thành câu trả lời.");
                throw new InvalidOperationException("Gemini không trả về nội dung. Vui lòng thử lại.");
            }
            return aiResponse.Trim();
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new JsonException("Lỗi định dạng response từ Gemini API", ex);
        }
    }
}
