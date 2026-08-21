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
    private const int DefaultMaxRetryAttempts = 4;
    private const int DefaultBaseBackoffMilliseconds = 1000;

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

    private int GetMaxRetryAttempts() =>
        int.TryParse(_configuration["Gemini:MaxRetryAttempts"], out var value) && value is >= 1 and <= 6
            ? value
            : DefaultMaxRetryAttempts;

    private int GetBaseBackoffMilliseconds() =>
        int.TryParse(_configuration["Gemini:RetryBaseDelayMilliseconds"], out var value) && value is >= 1 and <= 5000
            ? value
            : DefaultBaseBackoffMilliseconds;

    public async Task<GeminiResponseDto> GetGeminiResponseAsync(List<ChatMessageDto> messageHistory, string operation = "CHAT", CancellationToken cancellationToken = default)
    {
        string apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("LỖI BẢO MẬT: Chưa cấu hình GEMINI_API_KEY trong biến môi trường hoặc file appsettings.json!");
        }

        string model = (_configuration["Gemini:Model"] ?? "gemini-3.1-flash-lite").Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = "gemini-3.1-flash-lite";
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

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        string responseString;
        int attempt = 0;
        int maxRetryAttempts = GetMaxRetryAttempts();
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
            catch (OperationCanceledException) when (attempt < maxRetryAttempts)
            {
                await DelayBeforeRetryAsync(attempt, null, cancellationToken);
                continue;
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException($"Gemini API không phản hồi sau {GetTimeoutSeconds()}s (đã thử {attempt} lần).");
            }
            catch (HttpRequestException) when (attempt < maxRetryAttempts)
            {
                await DelayBeforeRetryAsync(attempt, null, cancellationToken);
                continue;
            }

            bool isTransientStatus = response.StatusCode == HttpStatusCode.TooManyRequests ||
                                      (int)response.StatusCode >= 500;

            if (!response.IsSuccessStatusCode)
            {
                if (isTransientStatus && attempt < maxRetryAttempts)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta;
                    response.Dispose();
                    await DelayBeforeRetryAsync(attempt, retryAfter, cancellationToken);
                    continue;
                }

                string errorDetails = await response.Content.ReadAsStringAsync(cancellationToken);
                response.Dispose();
                throw new HttpRequestException(
                    $"Lỗi gọi Gemini API (HTTP {response.StatusCode}): {errorDetails}",
                    inner: null,
                    statusCode: response.StatusCode);
            }

            responseString = await response.Content.ReadAsStringAsync(cancellationToken);
            response.Dispose();
            break;
        }

        stopwatch.Stop();
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

            // Extract usageMetadata
            int promptTokens = jsonNode?["usageMetadata"]?["promptTokenCount"]?.GetValue<int>() ?? 0;
            int completionTokens = jsonNode?["usageMetadata"]?["candidatesTokenCount"]?.GetValue<int>() ?? 0;
            int cachedTokens = jsonNode?["usageMetadata"]?["cachedContentTokenCount"]?.GetValue<int>() ?? 0;
            int totalTokens = jsonNode?["usageMetadata"]?["totalTokenCount"]?.GetValue<int>() ?? (promptTokens + completionTokens);

            // Estimated Cost for 1.5/2.0/3.0 flash: $0.075 / 1M prompt, $0.30 / 1M completion
            decimal estimatedCost = Math.Round((promptTokens * 0.000000075m) + (completionTokens * 0.00000030m), 6);

            return new GeminiResponseDto
            {
                Content = aiResponse.Trim(),
                Provider = "Google",
                Model = model,
                Operation = operation,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                CachedTokens = cachedTokens,
                TotalTokens = totalTokens,
                LatencyMs = stopwatch.ElapsedMilliseconds,
                Status = "SUCCESS",
                EstimatedCost = estimatedCost,
                Currency = "USD",
                PricingVersion = "2026.1",
                RequestId = Guid.NewGuid().ToString("N")
            };
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

    private Task DelayBeforeRetryAsync(int attempt, TimeSpan? retryAfter, CancellationToken cancellationToken)
    {
        var exponentialDelay = TimeSpan.FromMilliseconds(
            GetBaseBackoffMilliseconds() * Math.Pow(2, attempt - 1) + Random.Shared.Next(0, 251));
        var delay = retryAfter.HasValue && retryAfter.Value > exponentialDelay
            ? retryAfter.Value
            : exponentialDelay;
        return Task.Delay(delay, cancellationToken);
    }

    public async Task<string> ExtractTextFromImageAsync(byte[] imageBytes, string mimeType, CancellationToken cancellationToken = default)
    {
        string apiKey = GetApiKey();
        if (string.IsNullOrEmpty(apiKey))
        {
            throw new InvalidOperationException("LỖI BẢO MẬT: Chưa cấu hình GEMINI_API_KEY trong biến môi trường hoặc file appsettings.json!");
        }

        string model = (_configuration["Gemini:Model"] ?? "gemini-2.5-flash").Trim();
        if (string.IsNullOrWhiteSpace(model))
            model = "gemini-2.5-flash";

        string apiUrl = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";

        string base64Data = Convert.ToBase64String(imageBytes);

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new
                        {
                            inlineData = new
                            {
                                mimeType = string.IsNullOrWhiteSpace(mimeType) ? "image/jpeg" : mimeType,
                                data = base64Data
                            }
                        },
                        new
                        {
                            text = "Hãy trích xuất và số hóa toàn bộ nội dung trong hình ảnh này một cách đầy đủ và chính xác nhất bằng tiếng Việt. " +
                                   "Bao gồm toàn bộ chữ viết, ghi chú, tiêu đề, công thức toán học/khoa học (dưới dạng LaTeX nếu có), " +
                                   "dữ liệu bảng biểu, và mô tả tóm tắt các sơ đồ/hình minh họa nếu có trong tài liệu. " +
                                   "Không thêm lời bình luận ngoài lề của bạn, chỉ trả về nội dung trích xuất của tài liệu."
                        }
                    }
                }
            },
            generationConfig = new
            {
                maxOutputTokens = 4096
            }
        };

        string jsonInputString = JsonSerializer.Serialize(payload);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, apiUrl)
        {
            Content = new StringContent(jsonInputString, Encoding.UTF8, "application/json")
        };
        requestMessage.Headers.Add("x-goog-api-key", apiKey);

        var response = await _httpClient.SendAsync(requestMessage, linkedCts.Token);
        string responseBody = await response.Content.ReadAsStringAsync(linkedCts.Token);

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gemini Vision OCR Error ({response.StatusCode}): {responseBody}");
        }

        var jsonNode = JsonNode.Parse(responseBody);
        string extracted = jsonNode?["candidates"]?[0]?["content"]?["parts"]?[0]?["text"]?.GetValue<string>() ?? "";
        return extracted.Trim();
    }
}
