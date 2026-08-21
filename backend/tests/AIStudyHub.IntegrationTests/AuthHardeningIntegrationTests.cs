using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudyHub.IntegrationTests;

public class AuthHardeningIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AuthHardeningIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<User> SeedUserAsync(string email, string rawPassword = "Password123!")
    {
        await _factory.SeedDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var existing = await db.Users.FirstOrDefaultAsync(u => u.Email == email.ToLowerInvariant());
        if (existing != null) return existing;

        var user = new User
        {
            Username = "authuser_" + Guid.NewGuid().ToString("N")[..6],
            Email = email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(rawPassword),
            Role = "STUDENT",
            Status = "ACTIVE",
            TierId = 1,
            Balance = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Login_And_Refresh_With_Opaque_Token_Succeeds()
    {
        var user = await SeedUserAsync("hardened_login@test.com");

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = user.Email,
            password = "Password123!",
            rememberMe = true
        });
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);

        var loginJson = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(loginJson.TryGetProperty("refreshToken", out var refreshProp));
        var oldRefreshToken = refreshProp.GetString();
        Assert.False(string.IsNullOrWhiteSpace(oldRefreshToken));

        // Call refresh endpoint
        var refreshRes = await _client.PostAsJsonAsync("/api/auth/refresh", new
        {
            refreshToken = oldRefreshToken
        });
        Assert.Equal(HttpStatusCode.OK, refreshRes.StatusCode);

        var refreshJson = await refreshRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(refreshJson.TryGetProperty("refreshToken", out var newRefreshProp));
        var newRefreshToken = newRefreshProp.GetString();
        Assert.NotNull(newRefreshToken);
        Assert.NotEqual(oldRefreshToken, newRefreshToken);
    }

    [Fact]
    public async Task Replay_Attack_On_Old_Refresh_Token_Revokes_Session_Family()
    {
        var user = await SeedUserAsync("replay_victim@test.com");

        // 1. Initial Login
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = user.Email,
            password = "Password123!",
            rememberMe = true
        });
        var loginJson = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var token1 = loginJson.GetProperty("refreshToken").GetString();

        // 2. Legitimate Refresh -> token1 rotated to token2
        var refreshRes1 = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token1 });
        var refreshJson1 = await refreshRes1.Content.ReadFromJsonAsync<JsonElement>();
        var token2 = refreshJson1.GetProperty("refreshToken").GetString();

        // 3. Attacker replays token1
        var replayRes = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token1 });
        Assert.Equal(HttpStatusCode.Unauthorized, replayRes.StatusCode);

        // 4. Token2 should now also fail because family was revoked as COMPROMISED
        var token2Try = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token2 });
        Assert.Equal(HttpStatusCode.Unauthorized, token2Try.StatusCode);
    }

    [Fact]
    public async Task Logout_Invalidates_Refresh_Token()
    {
        var user = await SeedUserAsync("logout_user@test.com");

        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = user.Email,
            password = "Password123!",
            rememberMe = true
        });
        var loginJson = await loginRes.Content.ReadFromJsonAsync<JsonElement>();
        var token = loginJson.GetProperty("refreshToken").GetString();

        // Logout
        var logoutRes = await _client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = token });
        Assert.Equal(HttpStatusCode.OK, logoutRes.StatusCode);

        // Refresh attempt fails
        var refreshRes = await _client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = token });
        Assert.Equal(HttpStatusCode.Unauthorized, refreshRes.StatusCode);
    }
}
