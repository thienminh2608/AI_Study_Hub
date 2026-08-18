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
[Route("api/folder")]
public class FolderController : ControllerBase
{
    private readonly IFolderService _folderService;
    private readonly IPermissionService _permissionService;

    public FolderController(IFolderService folderService, IPermissionService permissionService)
    {
        _folderService = folderService;
        _permissionService = permissionService;
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

    [HttpGet]
    public async Task<IActionResult> GetChildFolders([FromQuery] int? parentFolderId)
    {
        try
        {
            int userId = GetCurrentUserId();
            var folders = await _folderService.GetChildFoldersAsync(userId, parentFolderId);
            return Ok(folders);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    [HttpGet("all")]
    public async Task<IActionResult> GetAllFolders()
    {
        try
        {
            int userId = GetCurrentUserId();
            var folders = await _folderService.GetAllUserFoldersAsync(userId);
            return Ok(folders);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetFolderById(int id)
    {
        var folder = await _folderService.GetFolderByIdAsync(id);
        if (folder == null)
        {
            return NotFound(new
            {
                message = "Thư mục không tồn tại."
            });
        }

        int userId = GetCurrentUserId();
        var effectiveRole = await _permissionService.GetEffectiveFolderRoleAsync(id, userId);
        if (folder.UserId != userId && effectiveRole == "NONE")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Bạn không có quyền truy cập thư mục này."
            });
        }

        return Ok(folder);
    }

    [HttpPost]
    public async Task<IActionResult> CreateFolder([FromBody] CreateFolderDto dto)
    {
        try
        {
            int userId = GetCurrentUserId();
            bool success = await _folderService.CreateFolderAsync(userId, dto);
            if (success)
            {
                return Ok(new
                {
                    message = "Đã tạo thư mục thành công."
                });
            }
            return BadRequest(new
            {
                message = "Không thể tạo thư mục."
            });
        }
        catch (InvalidOperationException ex)
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
                message = ex.Message
            });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateFolder(int id, [FromBody] UpdateFolderDto dto)
    {
        try
        {
            int userId = GetCurrentUserId();
            bool success = await _folderService.UpdateFolderAsync(userId, id, dto);
            if (success)
            {
                return Ok(new
                {
                    message = "Đã cập nhật thư mục thành công."
                });
            }
            return BadRequest(new
            {
                message = "Không thể cập nhật thư mục."
            });
        }
        catch (InvalidOperationException ex)
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
                message = ex.Message
            });
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFolder(int id)
    {
        try
        {
            int userId = GetCurrentUserId();
            bool success = await _folderService.DeleteFolderRecursiveAsync(userId, id);
            if (success)
            {
                return Ok(new
                {
                    message = "Đã xóa thư mục và toàn bộ tài liệu con thành công."
                });
            }
            return BadRequest(new
            {
                message = "Không thể xóa thư mục."
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
}
