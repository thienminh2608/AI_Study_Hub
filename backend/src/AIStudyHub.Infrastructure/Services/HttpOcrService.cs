using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace AIStudyHub.Infrastructure.Services;

public sealed class HttpOcrService : IOcrService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public HttpOcrService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<OcrResultDto> ExtractAsync(string filePath, string fileName, CancellationToken cancellationToken = default)
    {
        var endpoint = _configuration["Ocr:Endpoint"]?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint))
            return new OcrResultDto { IsConfigured = false };

        await using var stream = File.OpenRead(filePath);
        using var content = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", fileName);

        using var response = await _httpClient.PostAsync(endpoint, content, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OcrResultDto>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken);
        return result ?? new OcrResultDto { IsConfigured = true };
    }
}