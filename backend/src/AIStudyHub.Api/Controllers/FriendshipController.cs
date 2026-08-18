using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIStudyHub.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/friendship")]
public class FriendshipController : ControllerBase
{
    private readonly IFriendshipService _friendshipService;

    public FriendshipController(IFriendshipService friendshipService)
    {
        _friendshipService = friendshipService;
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

    [HttpPost("request")]
    public async Task<IActionResult> SendRequest([FromBody] SendFriendRequestDto dto)
    {
        int userId = GetCurrentUserId();
        if (userId == dto.AddresseeId)
        {
            return BadRequest(new
            {
                message = "Bạn không thể gửi lời mời kết bạn cho chính mình."
            });
        }

        bool success = await _friendshipService.SendFriendRequestAsync(userId, dto.AddresseeId);
        if (success)
        {
            return Ok(new
            {
                message = "Đã gửi lời mời kết bạn thành công."
            });
        }
        return BadRequest(new
        {
            message = "Không thể gửi lời mời kết bạn (có thể mối quan hệ đã tồn tại)."
        });
    }

    [HttpPost("respond")]
    public async Task<IActionResult> RespondToFriendship([FromQuery] int targetUserId, [FromQuery] string status)
    {
        int userId = GetCurrentUserId();
        bool success = await _friendshipService.UpdateFriendshipStatusAsync(userId, targetUserId, status);
        if (success)
        {
            return Ok(new
            {
                message = $"Đã cập nhật trạng thái kết bạn thành công ({status.ToUpper()})."
            });
        }
        return BadRequest(new
        {
            message = "Không thể cập nhật trạng thái kết bạn."
        });
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteFriendship([FromQuery] int targetUserId)
    {
        int userId = GetCurrentUserId();
        bool success = await _friendshipService.DeleteFriendshipAsync(userId, targetUserId);
        if (success)
        {
            return Ok(new
            {
                message = "Đã xóa mối quan hệ kết bạn / hủy chặn thành công."
            });
        }
        return BadRequest(new
        {
            message = "Không thể thực hiện yêu cầu (có thể bạn không có quyền hủy chặn)."
        });
    }

    [HttpGet("friends")]
    public async Task<IActionResult> GetFriends()
    {
        int userId = GetCurrentUserId();
        var friends = await _friendshipService.GetAcceptedFriendsAsync(userId);
        return Ok(friends);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingRequests()
    {
        int userId = GetCurrentUserId();
        var pending = await _friendshipService.GetPendingRequestsAsync(userId);
        return Ok(pending);
    }

    [HttpGet("blocked")]
    public async Task<IActionResult> GetBlockedUsers()
    {
        int userId = GetCurrentUserId();
        var blocked = await _friendshipService.GetBlockedUsersAsync(userId);
        return Ok(blocked);
    }

    [HttpGet("friends/paged")]
    public async Task<IActionResult> GetFriendsPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        int userId = GetCurrentUserId();
        var paged = await _friendshipService.GetAcceptedFriendsPagedAsync(userId, page, pageSize);
        return Ok(paged);
    }

    [HttpGet("pending/paged")]
    public async Task<IActionResult> GetPendingRequestsPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        int userId = GetCurrentUserId();
        var paged = await _friendshipService.GetPendingRequestsPagedAsync(userId, page, pageSize);
        return Ok(paged);
    }

    [HttpGet("blocked/paged")]
    public async Task<IActionResult> GetBlockedUsersPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 10)
    {
        int userId = GetCurrentUserId();
        var paged = await _friendshipService.GetBlockedUsersPagedAsync(userId, page, pageSize);
        return Ok(paged);
    }

    [HttpGet("find")]
    public async Task<IActionResult> FindFriendByEmail([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new
            {
                message = "Vui lòng cung cấp email."
            });
        }

        int userId = GetCurrentUserId();
        var friend = await _friendshipService.FindUserByEmailAsync(userId, email.Trim());
        if (friend == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy người dùng với email này."
            });
        }

        return Ok(friend);
    }
}
