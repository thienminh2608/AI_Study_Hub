using System.Net;
using System.Net.Http;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Infrastructure.Services.Gemini;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AIStudyHub.UnitTests;

public class GeminiAvailabilityTests
{
    [Fact]
    public async Task GetGeminiResponseAsync_RetriesTransient503_AndPreservesStatusCode()
    {
        var handler = new AlwaysUnavailableHandler();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "test-key",
            ["Gemini:MaxRetryAttempts"] = "3",
            ["Gemini:RetryBaseDelayMilliseconds"] = "1"
        }).Build();
        var service = new GeminiService(new HttpClient(handler), configuration);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetGeminiResponseAsync([
            new ChatMessageDto { Sender = "USER", MessageContent = "Test" }
        ]));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal(3, handler.RequestCount);
    }

    private sealed class AlwaysUnavailableHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("{\"error\":{\"status\":\"UNAVAILABLE\"}}")
            });
        }
    }
}
