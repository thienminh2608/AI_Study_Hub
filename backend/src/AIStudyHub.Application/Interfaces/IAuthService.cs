using System;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto?> RegisterAsync(RegisterDto dto);
    Task<AuthResponseDto?> LoginAsync(LoginDto dto);
    Task<AuthResponseDto?> RefreshAsync(string refreshToken);
    Task<bool> SendForgotPasswordOtpAsync(string email);
    Task<bool> VerifyOtpAsync(VerifyOtpDto dto);
    Task<bool> ResetPasswordAsync(ResetPasswordDto dto);
    Task<UserBalanceDto?> GetUserBalanceAndTierAsync(int userId);
    Task<UserBalanceDto?> UpdateUsernameAsync(int userId, string username);
}

public class UpdateUsernameDto
{
    public string Username { get; set; } = string.Empty;
}

public class UserBalanceDto
{
    public int UserId
    {
        get; set;
    }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Role { get; set; } = null!;
    public int Balance
    {
        get; set;
    }
    public int TierId
    {
        get; set;
    }
    public string TierName { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? ExpiresAt
    {
        get; set;
    }
    public bool IsAutoRenew
    {
        get; set;
    }
    public DateTime? GracePeriodEndsAt
    {
        get; set;
    }
}
