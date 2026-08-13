namespace AIStudyHub.Application.DTOs;

public class LoginDto
{
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;
    public bool RememberMe
    {
        get; set;
    }
}

public class RegisterDto
{
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Password { get; set; } = null!;
}

public class ForgotPasswordDto
{
    public string Email { get; set; } = null!;
}

public class VerifyOtpDto
{
    public string Email { get; set; } = null!;
    public string Otp { get; set; } = null!;
}

public class ResetPasswordDto
{
    public string Email { get; set; } = null!;
    public string NewPassword { get; set; } = null!;
}

public class AuthResponseDto
{
    public string Token { get; set; } = null!;
    public string? RefreshToken { get; set; }
    public int UserId
    {
        get; set;
    }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Role { get; set; } = null!;
    public int? TierId
    {
        get; set;
    }
    public int? Balance
    {
        get; set;
    }
}

public class RefreshTokenDto
{
    public string RefreshToken { get; set; } = null!;
}
