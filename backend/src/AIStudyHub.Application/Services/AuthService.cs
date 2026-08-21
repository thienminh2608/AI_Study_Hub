using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AIStudyHub.Application.Services;

public class AuthService : IAuthService
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IMailService _mailService;
    private readonly IConfiguration _configuration;
    private readonly IClock _clock;

    public AuthService(
        IStudyHubDbContext dbContext,
        IMailService mailService,
        IConfiguration configuration,
        IClock clock)
    {
        _dbContext = dbContext;
        _mailService = mailService;
        _configuration = configuration;
        _clock = clock;
    }

    private string GetOtpPepper()
    {
        var pepper = _configuration["Auth:OtpPepper"];
        if (string.IsNullOrWhiteSpace(pepper))
        {
            throw new InvalidOperationException("Auth:OtpPepper is not configured.");
        }
        return pepper;
    }

    private static bool IsValidPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8)
            return false;
        if (!Regex.IsMatch(password, @"[A-Z]"))
            return false;
        if (!Regex.IsMatch(password, @"[0-9]"))
            return false;
        if (!Regex.IsMatch(password, @"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]"))
            return false;
        return true;
    }

    public static string HashToken(string rawToken)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string HashEmail(string normalizedEmail)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ComputeOtpHmac(string pepper, string normalizedEmail, string otp, Guid challengeId)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pepper));
        var input = $"{normalizedEmail}:{otp}:{challengeId}";
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GenerateOpaqueToken(int bytesCount = 64)
    {
        var bytes = RandomNumberGenerator.GetBytes(bytesCount);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    public async Task<AuthResponseDto?> RegisterAsync(RegisterDto dto)
    {
        if (!IsValidPassword(dto.Password))
        {
            throw new ArgumentException("Mật khẩu yếu! Mật khẩu phải có ít nhất 8 ký tự, bao gồm chữ hoa, chữ số và ký tự đặc biệt.");
        }

        var normalizedEmail = NormalizeEmail(dto.Email);
        var existingUser = await _dbContext.Users.AnyAsync(u => u.Email == normalizedEmail);
        if (existingUser)
        {
            throw new ArgumentException("Email này đã được sử dụng.");
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        var user = new User
        {
            Username = dto.Username,
            Email = normalizedEmail,
            PasswordHash = passwordHash,
            Role = "STUDENT",
            TierId = 2, // Free Tier
            Balance = 0,
            AiPromptsToday = 0,
            LastPromptReset = _clock.Now,
            Status = "ACTIVE",
            CreatedAt = _clock.Now,
            UpdatedAt = _clock.Now
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        return new AuthResponseDto
        {
            Token = GenerateJwtToken(user),
            UserId = user.UserId,
            Username = user.Username,
            Email = user.Email ?? "",
            Role = user.Role ?? "STUDENT",
            TierId = user.TierId,
            Balance = user.Balance
        };
    }

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto, string? ipAddress = null, string? userAgent = null)
    {
        var inputIdentifier = (dto.Email ?? string.Empty).Trim();
        var normalizedEmail = NormalizeEmail(dto.Email);
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => 
            u.Email == normalizedEmail || 
            u.Username == inputIdentifier || 
            u.Username == normalizedEmail);
        if (user == null)
            return null;

        if ("BANNED".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Tài khoản của bạn đã bị khóa (BANNED).");
        }

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            return null;
        }

        string? rawRefreshToken = null;
        if (dto.RememberMe)
        {
            rawRefreshToken = GenerateOpaqueToken(64);
            var tokenHash = HashToken(rawRefreshToken);
            var expiryDays = double.TryParse(_configuration["Jwt:RefreshExpiryInDays"], out var exp) ? exp : 30;

            var session = new RefreshTokenSession
            {
                UserId = user.UserId,
                TokenFamilyId = Guid.NewGuid(),
                ParentSessionId = null,
                TokenHash = tokenHash,
                ExpiresAt = _clock.UtcNow.AddDays(expiryDays),
                CreatedAt = _clock.UtcNow,
                CreatedByIp = ipAddress,
                UserAgent = userAgent,
                IsUsed = false
            };

            _dbContext.RefreshTokenSessions.Add(session);
            await _dbContext.SaveChangesAsync();
        }

        return new AuthResponseDto
        {
            Token = GenerateJwtToken(user),
            RefreshToken = rawRefreshToken,
            UserId = user.UserId,
            Username = user.Username,
            Email = user.Email ?? "",
            Role = user.Role ?? "STUDENT",
            TierId = user.TierId,
            Balance = user.Balance
        };
    }

    public async Task<AuthResponseDto?> RefreshAsync(string refreshToken, string? ipAddress = null, string? userAgent = null)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        var tokenHash = HashToken(refreshToken);
        var session = await _dbContext.RefreshTokenSessions
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash);

        if (session == null)
        {
            // Legacy token sunset: Stateless JWT refresh tokens are rejected
            return null;
        }

        var now = _clock.UtcNow;

        // 1. Strict Reuse Detection: If an already-rotated token is presented
        if (session.RevokedReason == "ROTATED" || (session.IsUsed && session.RevokedAt != null && session.RevokedReason != "USER_LOGOUT" && session.RevokedReason != "PASSWORD_CHANGED"))
        {
            // Revoke all sessions in the entire TokenFamilyId with COMPROMISED
            var compromisedFamilySessions = await _dbContext.RefreshTokenSessions
                .Where(s => s.UserId == session.UserId && s.TokenFamilyId == session.TokenFamilyId && s.RevokedAt == null)
                .ToListAsync();

            foreach (var s in compromisedFamilySessions)
            {
                s.RevokedAt = now;
                s.RevokedReason = "COMPROMISED";
                s.RevokedByIp = ipAddress;
            }

            await _dbContext.SaveChangesAsync();
            return null;
        }

        // 2. Normal Revoked Session (e.g. USER_LOGOUT, PASSWORD_CHANGED, COMPROMISED)
        if (session.RevokedAt != null)
        {
            return null;
        }

        // 3. Expired Session
        if (session.ExpiresAt <= now)
        {
            session.RevokedAt = now;
            session.RevokedReason = "EXPIRED";
            await _dbContext.SaveChangesAsync();
            return null;
        }

        // 4. Verify User status
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == session.UserId);
        if (user == null || !"ACTIVE".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // 5. Atomic Conditional Rotation
        var newRawToken = GenerateOpaqueToken(64);
        var newHash = HashToken(newRawToken);
        var expiryDays = double.TryParse(_configuration["Jwt:RefreshExpiryInDays"], out var exp) ? exp : 30;

        session.IsUsed = true;
        session.LastUsedAt = now;
        session.RevokedAt = now;
        session.RevokedReason = "ROTATED";
        session.RevokedByIp = ipAddress;
        session.ReplacedByTokenHash = newHash;

        var newSession = new RefreshTokenSession
        {
            UserId = session.UserId,
            TokenFamilyId = session.TokenFamilyId,
            ParentSessionId = session.SessionId,
            TokenHash = newHash,
            ExpiresAt = now.AddDays(expiryDays),
            CreatedAt = now,
            CreatedByIp = ipAddress,
            UserAgent = userAgent,
            IsUsed = false
        };

        _dbContext.RefreshTokenSessions.Add(newSession);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            _dbContext.ChangeTracker.Clear();
            var reloaded = await _dbContext.RefreshTokenSessions.AsNoTracking()
                .FirstOrDefaultAsync(s => s.TokenHash == tokenHash);

            if (reloaded != null && (reloaded.RevokedReason == "ROTATED" || reloaded.IsUsed))
            {
                // Concurrent request already rotated this token -> Strict reuse attack detection: revoke entire family
                await _dbContext.RefreshTokenSessions
                    .Where(s => s.UserId == reloaded.UserId && s.TokenFamilyId == reloaded.TokenFamilyId && s.RevokedAt == null)
                    .ExecuteUpdateAsync(setters => setters
                        .SetProperty(s => s.RevokedAt, now)
                        .SetProperty(s => s.RevokedReason, "COMPROMISED")
                        .SetProperty(s => s.RevokedByIp, ipAddress));
            }

            return null;
        }

        return new AuthResponseDto
        {
            Token = GenerateJwtToken(user),
            RefreshToken = newRawToken,
            UserId = user.UserId,
            Username = user.Username,
            Email = user.Email ?? string.Empty,
            Role = user.Role ?? "STUDENT",
            TierId = user.TierId,
            Balance = user.Balance
        };
    }

    public async Task<bool> LogoutAsync(string? refreshToken, string? ipAddress = null)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return true;

        var tokenHash = HashToken(refreshToken);
        var session = await _dbContext.RefreshTokenSessions
            .FirstOrDefaultAsync(s => s.TokenHash == tokenHash);

        if (session != null && session.RevokedAt == null)
        {
            session.RevokedAt = _clock.UtcNow;
            session.RevokedReason = "USER_LOGOUT";
            session.RevokedByIp = ipAddress;
            await _dbContext.SaveChangesAsync();
        }

        return true;
    }

    public async Task<bool> RevokeAllUserSessionsAsync(int userId, string reason)
    {
        var now = _clock.UtcNow;
        var activeSessions = await _dbContext.RefreshTokenSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ToListAsync();

        foreach (var s in activeSessions)
        {
            s.RevokedAt = now;
            s.RevokedReason = reason;
        }

        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<SendOtpResponseDto> SendForgotPasswordOtpAsync(string email)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user == null)
        {
            // Anti-enumeration: return generic success message even if email not registered
            return new SendOtpResponseDto
            {
                Success = true,
                Message = "Nếu email tồn tại trên hệ thống, mã xác nhận OTP sẽ được gửi đến hộp thư của bạn."
            };
        }

        var emailHash = HashEmail(normalizedEmail);
        var now = _clock.UtcNow;
        var newCooldown = now.AddSeconds(60);

        // 1. Atomic Rate-Limit Check & Claim via auth_otp_rate_limits table
        var claimed = await _dbContext.AuthOtpRateLimits
            .Where(r => r.NormalizedEmailHash == emailHash && r.Purpose == "PASSWORD_RESET" && r.CooldownUntil <= now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.CooldownUntil, newCooldown)
                .SetProperty(r => r.LastSentAt, now)
                .SetProperty(r => r.RequestCount, r => r.RequestCount + 1));

        if (claimed == 0)
        {
            // Row may already exist with active cooldown OR does not exist yet
            var existingRow = await _dbContext.AuthOtpRateLimits.AsNoTracking()
                .FirstOrDefaultAsync(r => r.NormalizedEmailHash == emailHash && r.Purpose == "PASSWORD_RESET");

            if (existingRow != null)
            {
                int secondsLeft = Math.Max(1, (int)Math.Ceiling((existingRow.CooldownUntil - now).TotalSeconds));
                return new SendOtpResponseDto
                {
                    Success = false,
                    Message = $"Vui lòng đợi {secondsLeft} giây trước khi yêu cầu mã OTP mới."
                };
            }

            // Insert new row for first-time send
            var newRateLimit = new AuthOtpRateLimit
            {
                NormalizedEmailHash = emailHash,
                Purpose = "PASSWORD_RESET",
                CooldownUntil = newCooldown,
                LastSentAt = now,
                RequestCount = 1
            };
            _dbContext.AuthOtpRateLimits.Add(newRateLimit);

            try
            {
                await _dbContext.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Unique collision from concurrent first-time insert
                _dbContext.ChangeTracker.Clear();
                return new SendOtpResponseDto
                {
                    Success = false,
                    Message = "Yêu cầu OTP đang được xử lý hoặc trong thời gian chờ. Vui lòng thử lại sau 60 giây."
                };
            }
        }

        // Invalidate prior active challenges
        var priorChallenges = await _dbContext.AuthOtpChallenges
            .Where(c => c.NormalizedEmailHash == emailHash && c.Purpose == "PASSWORD_RESET" && c.ConsumedAt == null)
            .ToListAsync();

        foreach (var pc in priorChallenges)
        {
            pc.ConsumedAt = now;
        }

        // Generate 6-digit OTP & Peppered HMAC
        var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
        var challengeId = Guid.NewGuid();
        var pepper = GetOtpPepper();
        var otpHmac = ComputeOtpHmac(pepper, normalizedEmail, otp, challengeId);

        var newChallenge = new AuthOtpChallenge
        {
            ChallengeId = challengeId,
            NormalizedEmailHash = emailHash,
            Purpose = "PASSWORD_RESET",
            OtpHash = otpHmac,
            Attempts = 0,
            MaxAttempts = 5,
            CooldownUntil = newCooldown,
            ExpiresAt = now.AddMinutes(2),
            CreatedAt = now
        };

        _dbContext.AuthOtpChallenges.Add(newChallenge);

        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Concurrent race condition collision on rate-limit PK -> Reject request
            _dbContext.ChangeTracker.Clear();
            return new SendOtpResponseDto
            {
                Success = false,
                Message = "Yêu cầu OTP đang được xử lý hoặc trong thời gian chờ. Vui lòng thử lại sau 60 giây."
            };
        }

        // 2. Send email AFTER database transaction is securely committed
        try
        {
            _mailService.SendOtp(normalizedEmail, otp);
        }
        catch
        {
            // Email delivery failure does not corrupt database state
        }

        return new SendOtpResponseDto
        {
            Success = true,
            ChallengeId = challengeId,
            Message = "Mã OTP đã được gửi đến email của bạn."
        };
    }

    public async Task<VerifyOtpResponseDto> VerifyOtpAsync(VerifyOtpDto dto)
    {
        var normalizedEmail = NormalizeEmail(dto.Email);
        var emailHash = HashEmail(normalizedEmail);
        var now = _clock.UtcNow;

        AuthOtpChallenge? challenge = null;
        if (dto.ChallengeId.HasValue)
        {
            challenge = await _dbContext.AuthOtpChallenges
                .FirstOrDefaultAsync(c => c.ChallengeId == dto.ChallengeId.Value);
        }
        else
        {
            challenge = await _dbContext.AuthOtpChallenges
                .Where(c => c.NormalizedEmailHash == emailHash && c.Purpose == "PASSWORD_RESET" && c.ConsumedAt == null)
                .OrderByDescending(c => c.CreatedAt)
                .FirstOrDefaultAsync();
        }

        if (challenge == null || challenge.ConsumedAt != null || challenge.ExpiresAt <= now)
        {
            return new VerifyOtpResponseDto
            {
                Success = false,
                Message = "Mã OTP đã hết hạn hoặc không tồn tại."
            };
        }

        if (challenge.Attempts >= challenge.MaxAttempts)
        {
            return new VerifyOtpResponseDto
            {
                Success = false,
                Message = "Bạn đã nhập sai OTP quá 5 lần. Vui lòng yêu cầu mã xác nhận mới."
            };
        }

        challenge.Attempts++;

        var pepper = GetOtpPepper();
        var expectedHash = ComputeOtpHmac(pepper, normalizedEmail, dto.Otp, challenge.ChallengeId);

        bool matched = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedHash),
            Encoding.UTF8.GetBytes(challenge.OtpHash));

        if (!matched)
        {
            await _dbContext.SaveChangesAsync();
            var remaining = challenge.MaxAttempts - challenge.Attempts;
            return new VerifyOtpResponseDto
            {
                Success = false,
                Message = remaining > 0 
                    ? $"Mã OTP không chính xác. Bạn còn {remaining} lần thử." 
                    : "Bạn đã nhập sai OTP quá 5 lần. Vui lòng yêu cầu mã xác nhận mới."
            };
        }

        // OTP is valid -> consume challenge and issue one-time PasswordResetGrant
        challenge.ConsumedAt = now;

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user == null)
        {
            return new VerifyOtpResponseDto
            {
                Success = false,
                Message = "Không tìm thấy tài khoản người dùng."
            };
        }

        var resetGrantToken = GenerateOpaqueToken(32);
        var grantHash = HashToken(resetGrantToken);

        var grant = new PasswordResetGrant
        {
            UserId = user.UserId,
            ChallengeId = challenge.ChallengeId,
            GrantHash = grantHash,
            ExpiresAt = now.AddMinutes(10),
            IsConsumed = false,
            CreatedAt = now
        };

        _dbContext.PasswordResetGrants.Add(grant);
        await _dbContext.SaveChangesAsync();

        return new VerifyOtpResponseDto
        {
            Success = true,
            ResetGrantToken = resetGrantToken,
            Message = "Xác minh OTP thành công."
        };
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.ResetGrantToken))
        {
            throw new UnauthorizedAccessException("Thao tác đổi mật khẩu chưa được xác thực OTP (thiếu grant token).");
        }

        if (!IsValidPassword(dto.NewPassword))
        {
            throw new ArgumentException("Mật khẩu mới yếu! Mật khẩu phải có ít nhất 8 ký tự, bao gồm chữ hoa, chữ số và ký tự đặc biệt.");
        }

        var normalizedEmail = NormalizeEmail(dto.Email);
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user == null)
            return false;

        var grantHash = HashToken(dto.ResetGrantToken);
        var now = _clock.UtcNow;

        var grant = await _dbContext.PasswordResetGrants
            .FirstOrDefaultAsync(g => g.GrantHash == grantHash && g.UserId == user.UserId);

        if (grant == null || grant.IsConsumed || grant.ExpiresAt <= now)
        {
            throw new UnauthorizedAccessException("Phiếu xác thực đổi mật khẩu không hợp lệ hoặc đã hết hạn.");
        }

        grant.IsConsumed = true;
        grant.ConsumedAt = now;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt = _clock.Now;

        // Revoke all active sessions for this user on password change
        var activeSessions = await _dbContext.RefreshTokenSessions
            .Where(s => s.UserId == user.UserId && s.RevokedAt == null)
            .ToListAsync();

        foreach (var s in activeSessions)
        {
            s.RevokedAt = now;
            s.RevokedReason = "PASSWORD_CHANGED";
        }

        await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<UserBalanceDto?> GetUserBalanceAndTierAsync(int userId)
    {
        var user = await _dbContext.Users
            .Include(u => u.Tier)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null)
            return null;

        return new UserBalanceDto
        {
            UserId = user.UserId,
            Username = user.Username,
            Email = user.Email ?? string.Empty,
            Role = user.Role ?? "STUDENT",
            Balance = user.Balance ?? 0,
            TierId = user.TierId ?? 2,
            TierName = user.Tier?.TierName ?? "Free",
            Status = user.Status ?? "ACTIVE",
            ExpiresAt = user.ExpiresAt,
            IsAutoRenew = user.IsAutoRenew,
            GracePeriodEndsAt = user.GracePeriodEndsAt
        };
    }

    public async Task<UserBalanceDto?> UpdateUsernameAsync(int userId, string username)
    {
        var normalized = username?.Trim() ?? string.Empty;
        if (normalized.Length < 3 || normalized.Length > 50)
            throw new ArgumentException("Tên người dùng phải có từ 3 đến 50 ký tự.");

        if (await _dbContext.Users.AnyAsync(u => u.UserId != userId && u.Username == normalized))
            throw new ArgumentException("Tên người dùng này đã được sử dụng.");

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId);
        if (user == null)
            return null;

        user.Username = normalized;
        user.UpdatedAt = _clock.Now;
        await _dbContext.SaveChangesAsync();
        return await GetUserBalanceAndTierAsync(userId);
    }

    private string GenerateJwtToken(User user)
    {
        var configuredKey = _configuration["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured.");
        var key = Encoding.UTF8.GetBytes(configuredKey);
        var tokenHandler = new JwtSecurityTokenHandler();

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.Name, user.Username),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
            new Claim(ClaimTypes.Role, user.Role ?? "STUDENT")
        };

        var now = _clock.UtcNow;
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(double.Parse(_configuration["Jwt:ExpiryInMinutes"] ?? "180")),
            Issuer = _configuration["Jwt:Issuer"],
            Audience = _configuration["Jwt:Audience"],
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private static string NormalizeEmail(string email) =>
        (email ?? string.Empty).Trim().ToLowerInvariant();
}
