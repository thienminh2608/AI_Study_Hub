using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface IAuthService
{
    Task<AuthResponseDto?> RegisterAsync(RegisterDto dto);
    Task<AuthResponseDto?> LoginAsync(LoginDto dto);
    Task<bool> SendForgotPasswordOtpAsync(string email);
    Task<bool> VerifyOtpAsync(VerifyOtpDto dto);
    Task<bool> ResetPasswordAsync(ResetPasswordDto dto);
    Task<UserBalanceDto?> GetUserBalanceAndTierAsync(int userId);
}

public class UserBalanceDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = null!;
    public int Balance { get; set; }
    public int TierId { get; set; }
    public string TierName { get; set; } = null!;
    public string Status { get; set; } = null!;
}
