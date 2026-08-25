using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIStudyHub.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/chat")]
public class ChatBotController : ControllerBase
{
    private readonly IChatService _chatService;

    public ChatBotController(IChatService chatService)
    {
        _chatService = chatService;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !int.TryParse(claim.Value, out int userId))
        {
            throw new UnauthorizedAccessException("Không xác định được danh tính người dùng.");
        }
        return userId;
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions()
    {
        try
        {
            int userId = GetCurrentUserId();
            var sessions = await _chatService.GetUserSessionsAsync(userId);
            return Ok(sessions);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    [HttpPost("sessions")]
    public async Task<IActionResult> CreateSession([FromBody] CreateSessionDto dto)
    {
        try
        {
            int userId = GetCurrentUserId();
            var session = await _chatService.CreateSessionAsync(userId, dto);
            return Ok(session);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    [HttpPost("sessions/{sessionId}/pin")]
    public async Task<IActionResult> PinSession(int sessionId, [FromQuery] bool pin)
    {
        try
        {
            int userId = GetCurrentUserId();
            bool success = await _chatService.PinSessionAsync(userId, sessionId, pin);
            if (success)
            {
                return Ok(new
                {
                    message = pin ? "Đã ghim phiên chat." : "Đã bỏ ghim phiên chat."
                });
            }
            return BadRequest(new
            {
                message = "Không thể thay đổi trạng thái ghim."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    [HttpDelete("sessions/{sessionId}")]
    public async Task<IActionResult> DeleteSession(int sessionId)
    {
        try
        {
            int userId = GetCurrentUserId();
            bool success = await _chatService.DeleteSessionAsync(userId, sessionId);
            if (success)
            {
                return Ok(new
                {
                    message = "Đã xóa phiên chat."
                });
            }
            return BadRequest(new
            {
                message = "Không thể xóa phiên chat."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    [HttpPut("sessions/{sessionId}/document")]
    public async Task<IActionResult> SetAttachedDocument(int sessionId, [FromBody] SetChatDocumentDto dto)
    {
        try
        {
            var result = await _chatService.SetAttachedDocumentAsync(GetCurrentUserId(), sessionId, dto.DocumentId, dto.DocumentVersionId);
            return result == null ? NotFound(new { message = "Không tìm thấy phiên chat." }) : Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet("citations/{citationId}/resolve")]
    public async Task<IActionResult> ResolveCitation(long citationId, CancellationToken cancellationToken)
    {
        try
        {
            int userId = GetCurrentUserId();
            var result = await _chatService.ResolveCitationAsync(userId, citationId, cancellationToken);
            return result == null 
                ? NotFound(new { message = "Không tìm thấy trích dẫn hoặc bạn không có quyền truy cập." }) 
                : Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("sessions/{sessionId}/messages")]
    public async Task<IActionResult> GetMessages(int sessionId)
    {
        try
        {
            int userId = GetCurrentUserId();
            var messages = await _chatService.GetSessionMessagesAsync(userId, sessionId);
            return Ok(messages);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    [HttpPost("sessions/{sessionId}/ask")]
    public async Task<IActionResult> AskQuestion(int sessionId, [FromBody] AskQuestionDto dto, CancellationToken cancellationToken)
    {
        try
        {
            int userId = GetCurrentUserId();
            var result = await _chatService.ProcessUserMessageAsync(userId, sessionId, dto, cancellationToken);
            return Ok(new
            {
                response = result.Response,
                citations = result.Citations
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Hết lượt câu hỏi", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status429TooManyRequests, new
            {
                message = ex.Message,
                code = "AI_QUOTA_EXHAUSTED",
                retryable = false
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("LỖI BẢO MẬT:", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = ex.Message,
                code = "AI_CONFIGURATION_ERROR",
                retryable = false
            });
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = ex.Message,
                code = "AI_PROCESSING_ERROR",
                retryable = true
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (OperationCanceledException)
        {
            return StatusCode(499, new
            {
                message = "Yêu cầu đã bị hủy."
            });
        }
        catch (TimeoutException ex)
        {
            return StatusCode(StatusCodes.Status504GatewayTimeout, new
            {
                message = ex.Message,
                code = "AI_TIMEOUT",
                retryable = true
            });
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
                                              (int?)ex.StatusCode >= 500)
        {
            Response.Headers.RetryAfter = "5";
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                message = "Dịch vụ AI đang quá tải tạm thời. Vui lòng nhấn Thử lại sau vài giây.",
                code = "AI_PROVIDER_UNAVAILABLE",
                retryable = true,
                retryAfterSeconds = 5
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = $"Lỗi kết nối AI: {ex.Message}",
                code = "AI_SYSTEM_ERROR",
                retryable = true
            });
        }
    }
}
