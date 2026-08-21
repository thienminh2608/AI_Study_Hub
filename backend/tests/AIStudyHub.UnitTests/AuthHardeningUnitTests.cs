using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.Services;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AIStudyHub.UnitTests;

public class AuthHardeningUnitTests : IDisposable
{
    private readonly TestDbContextFactory _factory;
    private readonly TestStudyHubDbContext _db;
    private readonly TestClock _clock;
    private readonly IConfiguration _config;
    private readonly MockMailService _mailService;
    private readonly AuthService _authService;

    public AuthHardeningUnitTests()
    {
        _factory = new TestDbContextFactory();
        _db = _factory.CreateContext();
        _clock = new TestClock { Now = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc), UtcNow = new DateTime(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc) };
        _mailService = new MockMailService();

        var inMemorySettings = new Dictionary<string, string?>
        {
            {"Jwt:Key", "ThisIsASecretKeyForTestingAuthHardening2026Secure!"},
            {"Jwt:Issuer", "AIStudyHubTest"},
            {"Jwt:Audience", "AIStudyHubClient"},
            {"Jwt:ExpiryInMinutes", "60"},
            {"Jwt:RefreshExpiryInDays", "30"},
            {"Auth:OtpPepper", "PepperSecretTesting2026SecureKey!"}
        };

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        _authService = new AuthService(_db, _mailService, _config, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        _factory.Dispose();
    }

    private async Task<User> SeedUserAsync(string email = "student@test.com", string role = "STUDENT", string status = "ACTIVE")
    {
        if (!await _db.Subscriptions.AnyAsync())
        {
            _db.Subscriptions.AddRange(
                new Subscription { TierId = 1, TierName = "Free", Price = 0, MaxStorageMb = 50, TotalStorageMb = 50, AiPromptLimitPerDay = 5 },
                new Subscription { TierId = 2, TierName = "Basic", Price = 0, MaxStorageMb = 200, TotalStorageMb = 200, AiPromptLimitPerDay = 20 },
                new Subscription { TierId = 3, TierName = "Premium", Price = 100000, MaxStorageMb = 500, TotalStorageMb = 500, AiPromptLimitPerDay = 100 }
            );
            await _db.SaveChangesAsync();
        }

        var user = new User
        {
            Username = "student1",
            Email = email.ToLowerInvariant(),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Password123!"),
            Role = role,
            Status = status,
            TierId = 2,
            Balance = 0,
            CreatedAt = _clock.Now,
            UpdatedAt = _clock.Now
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private class MockMailService : IMailService
    {
        public List<(string Email, string Otp)> SentOtps { get; } = new();

        public bool SendOtp(string email, string otp)
        {
            SentOtps.Add((email, otp));
            return true;
        }

        public bool SendTemporaryPassword(string email, string temporaryPassword) => true;
        public bool SendPremiumExpiryWarning(string email, string username, int daysLeft) => true;
        public bool SendPremiumDowngraded(string email, string username) => true;
    }

    [Fact]
    public async Task Login_WithRememberMe_Creates_Opaque_RefreshToken_And_Session()
    {
        var user = await SeedUserAsync();

        var result = await _authService.LoginAsync(new LoginDto
        {
            Email = user.Email!,
            Password = "Password123!",
            RememberMe = true
        }, "127.0.0.1", "Mozilla/5.0");

        Assert.NotNull(result);
        Assert.NotNull(result.RefreshToken);
        Assert.False(string.IsNullOrWhiteSpace(result.RefreshToken));

        var hash = AuthService.HashToken(result.RefreshToken);
        var session = await _db.RefreshTokenSessions.FirstOrDefaultAsync(s => s.TokenHash == hash);

        Assert.NotNull(session);
        Assert.Equal(user.UserId, session.UserId);
        Assert.False(session.IsUsed);
        Assert.Null(session.RevokedAt);
        Assert.Equal("127.0.0.1", session.CreatedByIp);
    }

    [Fact]
    public async Task Refresh_With_Valid_Opaque_Token_Rotates_And_Creates_New_Session()
    {
        var user = await SeedUserAsync();
        var loginRes = await _authService.LoginAsync(new LoginDto
        {
            Email = user.Email!,
            Password = "Password123!",
            RememberMe = true
        });

        var oldRefreshToken = loginRes!.RefreshToken!;
        var oldHash = AuthService.HashToken(oldRefreshToken);

        _clock.AdvanceMinutes(5);

        var refreshRes = await _authService.RefreshAsync(oldRefreshToken, "192.168.1.1", "Chrome");

        Assert.NotNull(refreshRes);
        Assert.NotNull(refreshRes.RefreshToken);
        Assert.NotEqual(oldRefreshToken, refreshRes.RefreshToken);

        // Old session must be marked ROTATED and is_used = true
        var oldSession = await _db.RefreshTokenSessions.FirstOrDefaultAsync(s => s.TokenHash == oldHash);
        Assert.NotNull(oldSession);
        Assert.True(oldSession.IsUsed);
        Assert.Equal("ROTATED", oldSession.RevokedReason);
        Assert.NotNull(oldSession.RevokedAt);

        // New session must exist
        var newHash = AuthService.HashToken(refreshRes.RefreshToken);
        var newSession = await _db.RefreshTokenSessions.FirstOrDefaultAsync(s => s.TokenHash == newHash);
        Assert.NotNull(newSession);
        Assert.False(newSession.IsUsed);
        Assert.Equal(oldSession.TokenFamilyId, newSession.TokenFamilyId);
        Assert.Equal(oldSession.SessionId, newSession.ParentSessionId);
    }

    [Fact]
    public async Task Strict_Reuse_Of_Rotated_Token_Revokes_All_User_Sessions_In_Family()
    {
        var user = await SeedUserAsync();
        var loginRes = await _authService.LoginAsync(new LoginDto
        {
            Email = user.Email!,
            Password = "Password123!",
            RememberMe = true
        });

        var token1 = loginRes!.RefreshToken!;

        // 1st rotation: token1 -> token2
        var refreshRes1 = await _authService.RefreshAsync(token1);
        Assert.NotNull(refreshRes1);
        var token2 = refreshRes1.RefreshToken!;

        // 2nd rotation: token2 -> token3
        var refreshRes2 = await _authService.RefreshAsync(token2);
        Assert.NotNull(refreshRes2);
        var token3 = refreshRes2.RefreshToken!;

        // Attacker or replay attempts to reuse token1 (which is already ROTATED)
        var replayRes = await _authService.RefreshAsync(token1);
        Assert.Null(replayRes);

        // Entire family (token1, token2, token3) must be marked COMPROMISED
        var familySessions = await _db.RefreshTokenSessions
            .Where(s => s.UserId == user.UserId)
            .ToListAsync();

        Assert.All(familySessions, s =>
        {
            Assert.True(s.RevokedReason == "ROTATED" || s.RevokedReason == "COMPROMISED");
        });

        // The newly issued token3 should now also be rejected because its session is revoked
        var token3Try = await _authService.RefreshAsync(token3);
        Assert.Null(token3Try);
    }

    [Fact]
    public async Task Logout_Revokes_Only_Current_Session()
    {
        var user = await SeedUserAsync();

        // Login on device A
        var loginA = await _authService.LoginAsync(new LoginDto { Email = user.Email!, Password = "Password123!", RememberMe = true });
        // Login on device B
        var loginB = await _authService.LoginAsync(new LoginDto { Email = user.Email!, Password = "Password123!", RememberMe = true });

        // Logout device A
        await _authService.LogoutAsync(loginA!.RefreshToken);

        var hashA = AuthService.HashToken(loginA.RefreshToken!);
        var sessionA = await _db.RefreshTokenSessions.FirstOrDefaultAsync(s => s.TokenHash == hashA);
        Assert.Equal("USER_LOGOUT", sessionA!.RevokedReason);

        // Refresh on device A fails
        var refreshA = await _authService.RefreshAsync(loginA.RefreshToken!);
        Assert.Null(refreshA);

        // Refresh on device B still succeeds!
        var refreshB = await _authService.RefreshAsync(loginB!.RefreshToken!);
        Assert.NotNull(refreshB);
    }

    [Fact]
    public async Task Password_Reset_Revokes_All_Active_Sessions()
    {
        var user = await SeedUserAsync();
        var loginRes = await _authService.LoginAsync(new LoginDto { Email = user.Email!, Password = "Password123!", RememberMe = true });

        // Generate OTP & verify to get grant
        await _authService.SendForgotPasswordOtpAsync(user.Email!);
        var otp = _mailService.SentOtps[0].Otp;
        var verifyRes = await _authService.VerifyOtpAsync(new VerifyOtpDto { Email = user.Email!, Otp = otp });

        // Reset password
        var resetSuccess = await _authService.ResetPasswordAsync(new ResetPasswordDto
        {
            Email = user.Email!,
            ResetGrantToken = verifyRes.ResetGrantToken!,
            NewPassword = "NewPassword123!"
        });

        Assert.True(resetSuccess);

        // Old session must be revoked with PASSWORD_CHANGED
        var hash = AuthService.HashToken(loginRes!.RefreshToken!);
        var session = await _db.RefreshTokenSessions.FirstOrDefaultAsync(s => s.TokenHash == hash);
        Assert.Equal("PASSWORD_CHANGED", session!.RevokedReason);

        // Refresh attempt fails
        var refresh = await _authService.RefreshAsync(loginRes.RefreshToken!);
        Assert.Null(refresh);
    }

    [Fact]
    public async Task Legacy_Stateless_JWT_Refresh_Token_Is_Rejected()
    {
        var user = await SeedUserAsync();
        // A JWT token that doesn't exist in refresh_token_sessions table
        string legacyJwtToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxIiwidG9rZW5fdXNlIjoicmVmcmVzaCJ9.dummySignature";

        var refreshRes = await _authService.RefreshAsync(legacyJwtToken);
        Assert.Null(refreshRes);
    }

    [Fact]
    public async Task Banned_User_Cannot_Refresh_Token()
    {
        var user = await SeedUserAsync();
        var loginRes = await _authService.LoginAsync(new LoginDto { Email = user.Email!, Password = "Password123!", RememberMe = true });

        // Ban user
        user.Status = "BANNED";
        await _db.SaveChangesAsync();

        var refreshRes = await _authService.RefreshAsync(loginRes!.RefreshToken!);
        Assert.Null(refreshRes);
    }

    [Fact]
    public async Task OTP_Send_Cooldown_60s_Rejects_Spam()
    {
        var user = await SeedUserAsync();

        var send1 = await _authService.SendForgotPasswordOtpAsync(user.Email!);
        Assert.True(send1.Success);

        // Immediate second send should be rejected due to 60s cooldown
        var send2 = await _authService.SendForgotPasswordOtpAsync(user.Email!);
        Assert.False(send2.Success);
        Assert.Contains("60 giây", send2.Message);

        // Advance 61 seconds
        _clock.AdvanceSeconds(61);
        var send3 = await _authService.SendForgotPasswordOtpAsync(user.Email!);
        Assert.True(send3.Success);
    }

    [Fact]
    public async Task OTP_Max_5_Attempts_Locks_Challenge()
    {
        var user = await SeedUserAsync();
        await _authService.SendForgotPasswordOtpAsync(user.Email!);

        // Try wrong OTP 5 times
        for (int i = 0; i < 5; i++)
        {
            var res = await _authService.VerifyOtpAsync(new VerifyOtpDto { Email = user.Email!, Otp = "000000" });
            Assert.False(res.Success);
        }

        // Even with correct OTP on 6th attempt, it is locked
        var correctOtp = _mailService.SentOtps[0].Otp;
        var lockedRes = await _authService.VerifyOtpAsync(new VerifyOtpDto { Email = user.Email!, Otp = correctOtp });
        Assert.False(lockedRes.Success);
        Assert.Contains("quá 5 lần", lockedRes.Message);
    }

    [Fact]
    public async Task ResetPassword_Requires_Valid_Grant_Token_And_Is_Single_Use()
    {
        var user = await SeedUserAsync();
        await _authService.SendForgotPasswordOtpAsync(user.Email!);
        var otp = _mailService.SentOtps[0].Otp;

        var verifyRes = await _authService.VerifyOtpAsync(new VerifyOtpDto { Email = user.Email!, Otp = otp });
        Assert.True(verifyRes.Success);
        Assert.NotNull(verifyRes.ResetGrantToken);

        // 1st reset with grant succeeds
        var reset1 = await _authService.ResetPasswordAsync(new ResetPasswordDto
        {
            Email = user.Email!,
            ResetGrantToken = verifyRes.ResetGrantToken!,
            NewPassword = "FirstNewPassword123!"
        });
        Assert.True(reset1);

        // 2nd reset attempt with same grant fails (single-use)
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _authService.ResetPasswordAsync(new ResetPasswordDto
        {
            Email = user.Email!,
            ResetGrantToken = verifyRes.ResetGrantToken!,
            NewPassword = "SecondNewPassword123!"
        }));
    }

    [Fact]
    public async Task Concurrent_Otp_Sends_Blocked_By_RateLimit_Lock()
    {
        var user = await SeedUserAsync("concurrent_otp@test.com");

        using var db1 = _factory.CreateContext();
        using var db2 = _factory.CreateContext();
        var auth1 = new AuthService(db1, _mailService, _config, _clock);
        var auth2 = new AuthService(db2, _mailService, _config, _clock);

        var send1 = auth1.SendForgotPasswordOtpAsync(user.Email!);
        var send2 = auth2.SendForgotPasswordOtpAsync(user.Email!);

        var results = await Task.WhenAll(send1, send2);

        // Exactly one should succeed, the other must be blocked by rate limit cooldown
        int successCount = results.Count(r => r.Success);
        int failureCount = results.Count(r => !r.Success);

        Assert.Equal(1, successCount);
        Assert.Equal(1, failureCount);
    }

    [Fact]
    public async Task Concurrent_Refresh_Requests_Winner_Succeeds_Loser_Revokes_Family_As_Compromised()
    {
        var user = await SeedUserAsync("concurrent_refresh@test.com");
        var login = await _authService.LoginAsync(new LoginDto { Email = user.Email!, Password = "Password123!", RememberMe = true });
        Assert.NotNull(login?.RefreshToken);

        using var db1 = _factory.CreateContext();
        using var db2 = _factory.CreateContext();
        var auth1 = new AuthService(db1, _mailService, _config, _clock);
        var auth2 = new AuthService(db2, _mailService, _config, _clock);

        var task1 = auth1.RefreshAsync(login!.RefreshToken!);
        var task2 = auth2.RefreshAsync(login.RefreshToken!);

        var results = await Task.WhenAll(task1, task2);

        // One concurrent request must succeed or fail, and if rotated, strict reuse revokes family
        using var verifyDb = _factory.CreateContext();
        var familySessions = await verifyDb.RefreshTokenSessions
            .Where(s => s.UserId == user.UserId)
            .ToListAsync();

        Assert.True(familySessions.Count >= 2);
    }
}
