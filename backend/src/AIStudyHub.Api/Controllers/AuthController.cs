using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIStudyHub.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IStudyHubDbContext _db;

    public AuthController(IAuthService authService, IStudyHubDbContext db)
    {
        _authService = authService;
        _db = db;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try
        {
            var response = await _authService.RegisterAsync(dto);
            if (response == null)
            {
                return BadRequest(new
                {
                    message = "Đăng ký không thành công."
                });
            }
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = $"Lỗi hệ thống: {ex.Message}"
            });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers.UserAgent.ToString();
            var response = await _authService.LoginAsync(dto, ip, userAgent);
            if (response == null)
            {
                return Unauthorized(new
                {
                    message = "Email hoặc mật khẩu không chính xác."
                });
            }
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = $"Lỗi hệ thống: {ex.Message}"
            });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh([FromBody] RefreshTokenDto dto)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();
        var response = await _authService.RefreshAsync(dto.RefreshToken, ip, userAgent);
        return response == null
            ? Unauthorized(new { message = "Phiên ghi nhớ đăng nhập đã hết hạn. Vui lòng đăng nhập lại." })
            : Ok(response);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout([FromBody] LogoutDto? dto)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _authService.LogoutAsync(dto?.RefreshToken, ip);
        return Ok(new { message = "Đăng xuất thành công." });
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var result = await _authService.SendForgotPasswordOtpAsync(dto.Email);
        return Ok(new
        {
            success = result.Success,
            challengeId = result.ChallengeId,
            message = result.Message
        });
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        var result = await _authService.VerifyOtpAsync(dto);
        if (result.Success)
        {
            return Ok(new
            {
                success = true,
                resetGrantToken = result.ResetGrantToken,
                message = result.Message ?? "Xác thực OTP thành công."
            });
        }
        return BadRequest(new
        {
            success = false,
            message = result.Message ?? "Mã OTP không hợp lệ hoặc đã hết hạn."
        });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        try
        {
            bool success = await _authService.ResetPasswordAsync(dto);
            if (success)
            {
                return Ok(new
                {
                    message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại."
                });
            }
            return BadRequest(new
            {
                message = "Không thể đặt lại mật khẩu."
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = ex.Message
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = $"Lỗi hệ thống: {ex.Message}"
            });
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUserProfile()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(new
            {
                message = "Không tìm thấy token đăng nhập hợp lệ."
            });
        }

        var profile = await _authService.GetUserBalanceAndTierAsync(userId);
        if (profile == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy người dùng."
            });
        }

        return Ok(profile);
    }
    [Authorize]
    [HttpPut("profile/username")]
    public async Task<IActionResult> UpdateUsername([FromBody] UpdateUsernameDto dto)
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return Unauthorized();
        try
        {
            var profile = await _authService.UpdateUsernameAsync(userId, dto.Username);
            return profile == null ? NotFound() : Ok(profile);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    [Authorize]
    [HttpPost("subscription/toggle-autorenew")]
    public async Task<IActionResult> ToggleAutoRenew()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
            return Unauthorized();

        var user = await _db.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { message = "Không tìm thấy người dùng." });

        user.IsAutoRenew = !user.IsAutoRenew;
        await _db.SaveChangesAsync();

        return Ok(new
        {
            isAutoRenew = user.IsAutoRenew,
            message = user.IsAutoRenew ? "Đã bật tự động gia hạn thành công." : "Đã tắt tự động gia hạn thành công."
        });
    }
}
