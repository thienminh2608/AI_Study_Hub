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

    public AuthController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterDto dto)
    {
        try
        {
            var response = await _authService.RegisterAsync(dto);
            if (response == null)
            {
                return BadRequest(new { message = "Đăng ký không thành công." });
            }
            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Lỗi hệ thống: {ex.Message}" });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginDto dto)
    {
        try
        {
            var response = await _authService.LoginAsync(dto);
            if (response == null)
            {
                return Unauthorized(new { message = "Email hoặc mật khẩu không chính xác." });
            }
            return Ok(response);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Lỗi hệ thống: {ex.Message}" });
        }
    }

    [HttpPost("forgot-password")]
    public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        bool success = await _authService.SendForgotPasswordOtpAsync(dto.Email);
        if (success)
        {
            return Ok(new { message = "Mã OTP đã được gửi đến email của bạn. Vui lòng kiểm tra hộp thư." });
        }
        return BadRequest(new { message = "Email không tồn tại trong hệ thống." });
    }

    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        bool success = await _authService.VerifyOtpAsync(dto);
        if (success)
        {
            return Ok(new { message = "Xác thực OTP thành công. Bạn có thể tiến hành đổi mật khẩu." });
        }
        return BadRequest(new { message = "Mã OTP không hợp lệ hoặc đã hết hạn." });
    }

    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        try
        {
            bool success = await _authService.ResetPasswordAsync(dto);
            if (success)
            {
                return Ok(new { message = "Đặt lại mật khẩu thành công. Vui lòng đăng nhập lại." });
            }
            return BadRequest(new { message = "Không thể đặt lại mật khẩu." });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Lỗi hệ thống: {ex.Message}" });
        }
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrentUserProfile()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(new { message = "Không tìm thấy token đăng nhập hợp lệ." });
        }

        var profile = await _authService.GetUserBalanceAndTierAsync(userId);
        if (profile == null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        return Ok(profile);
    }
}
