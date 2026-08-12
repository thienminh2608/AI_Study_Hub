using AIStudyHub.Application.Interfaces;

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace AIStudyHub.Application.Services;

public class AuthService : IAuthService
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IMailService _mailService;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _configuration;

    public AuthService(
        IStudyHubDbContext dbContext,
        IMailService mailService,
        IMemoryCache cache,
        IConfiguration configuration)
    {
        _dbContext = dbContext;
        _mailService = mailService;
        _cache = cache;
        _configuration = configuration;
    }

    private bool IsValidPassword(string password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8) return false;
        if (!Regex.IsMatch(password, @"[A-Z]")) return false;
        if (!Regex.IsMatch(password, @"[0-9]")) return false;
        if (!Regex.IsMatch(password, @"[!@#$%^&*()_+\-=\[\]{};':""\\|,.<>\/?]")) return false;
        return true;
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
            LastPromptReset = DateTime.Now,
            Status = "ACTIVE",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
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

    public async Task<AuthResponseDto?> LoginAsync(LoginDto dto)
    {
        var normalizedEmail = NormalizeEmail(dto.Email);
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        if (user == null) return null;

        if ("BANNED".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Tài khoản của bạn đã bị khóa (BANNED).");
        }

        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            return null;
        }

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

    public async Task<bool> SendForgotPasswordOtpAsync(string email)
    {
        email = NormalizeEmail(email);
        var user = await _dbContext.Users.AnyAsync(u => u.Email == email);
        if (!user) return true;

        string sendLimitKey = $"OTP_SEND_{email}";
        if (_cache.TryGetValue(sendLimitKey, out _)) return true;
        _cache.Set(sendLimitKey, true, TimeSpan.FromSeconds(60));

        // Generate 6-digit OTP
        string otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();

        // Save OTP to cache with a 2-minute sliding expiration
        string cacheKey = $"OTP_{email}";
        _cache.Set(cacheKey, otp, TimeSpan.FromMinutes(2));

        // Send OTP email
        return _mailService.SendOtp(email, otp);
    }

    public async Task<bool> VerifyOtpAsync(VerifyOtpDto dto)
    {
        var email = NormalizeEmail(dto.Email);
        string cacheKey = $"OTP_{email}";
        string attemptKey = $"OTP_ATTEMPTS_{email}";
        var attempts = _cache.Get<int?>(attemptKey) ?? 0;
        if (attempts >= 5) return false;
        if (_cache.TryGetValue(cacheKey, out string? cachedOtp))
        {
            if (cachedOtp == dto.Otp)
            {
                // Set flag in cache that user is allowed to reset password
                string resetKey = $"ALLOW_RESET_{email}";
                _cache.Set(resetKey, true, TimeSpan.FromMinutes(10));
                _cache.Remove(cacheKey); // Consumed
                _cache.Remove(attemptKey);
                return true;
            }
        }
        _cache.Set(attemptKey, attempts + 1, TimeSpan.FromMinutes(2));
        return false;
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordDto dto)
    {
        var email = NormalizeEmail(dto.Email);
        string resetKey = $"ALLOW_RESET_{email}";
        if (!_cache.TryGetValue(resetKey, out bool allowed) || !allowed)
        {
            throw new UnauthorizedAccessException("Thao tác đổi mật khẩu chưa được xác thực OTP.");
        }

        if (!IsValidPassword(dto.NewPassword))
        {
            throw new ArgumentException("Mật khẩu mới yếu! Mật khẩu phải có ít nhất 8 ký tự, bao gồm chữ hoa, chữ số và ký tự đặc biệt.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null) return false;

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.Now;

        _cache.Remove(resetKey); // Clean up cache
        return await _dbContext.SaveChangesAsync() > 0;
    }

    public async Task<UserBalanceDto?> GetUserBalanceAndTierAsync(int userId)
    {
        var user = await _dbContext.Users
            .Include(u => u.Tier)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        if (user == null) return null;

        return new UserBalanceDto
        {
            UserId = user.UserId,
            Username = user.Username,
            Balance = user.Balance ?? 0,
            TierId = user.TierId ?? 2,
            TierName = user.Tier?.TierName ?? "Free",
            Status = user.Status ?? "ACTIVE"
        };
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

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(double.Parse(_configuration["Jwt:ExpiryInMinutes"] ?? "180")),
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
