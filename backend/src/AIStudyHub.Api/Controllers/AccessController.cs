using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AIStudyHub.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AccessController : ControllerBase
{
    private readonly IPermissionService _permissionService;

    public AccessController(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DOCUMENT ACCESS
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("document/{id}")]
    public async Task<IActionResult> GetDocumentAccess(int id)
    {
        var settings = await _permissionService.GetDocumentAccessSettingsAsync(id, GetCurrentUserId());
        return Ok(settings);
    }

    [HttpPut("document/{id}/general")]
    public async Task<IActionResult> UpdateDocumentGeneralAccess(int id, [FromBody] UpdateGeneralAccessRequest request)
    {
        await _permissionService.UpdateDocumentGeneralAccessAsync(id, request.GeneralAccess, GetCurrentUserId());
        return Ok(new { message = "General access updated successfully" });
    }

    [HttpPost("document/{id}/share")]
    public async Task<IActionResult> AddOrUpdateDocumentShare(int id, [FromBody] AddUserShareRequest request)
    {
        await _permissionService.AddOrUpdateDocumentUserShareAsync(id, request.Email, request.Role, GetCurrentUserId());
        return Ok(new { message = "User share updated successfully" });
    }

    [HttpDelete("document/{id}/share/{targetUserId}")]
    public async Task<IActionResult> RemoveDocumentShare(int id, int targetUserId)
    {
        await _permissionService.RemoveDocumentUserShareAsync(id, targetUserId, GetCurrentUserId());
        return Ok(new { message = "User share removed successfully" });
    }

    [HttpPost("document/{id}/link/rotate")]
    public async Task<IActionResult> RotateDocumentShareLink(int id)
    {
        var linkInfo = await _permissionService.RotateDocumentShareLinkAsync(id, GetCurrentUserId());
        return Ok(linkInfo);
    }

    [HttpPost("document/{id}/link/revoke")]
    public async Task<IActionResult> RevokeDocumentShareLink(int id)
    {
        await _permissionService.RevokeDocumentShareLinkAsync(id, GetCurrentUserId());
        return Ok(new { message = "Share link revoked successfully" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // FOLDER ACCESS
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("folder/{id}")]
    public async Task<IActionResult> GetFolderAccess(int id)
    {
        var settings = await _permissionService.GetFolderAccessSettingsAsync(id, GetCurrentUserId());
        return Ok(settings);
    }

    [HttpPut("folder/{id}/general")]
    public async Task<IActionResult> UpdateFolderGeneralAccess(int id, [FromBody] UpdateGeneralAccessRequest request)
    {
        await _permissionService.UpdateFolderGeneralAccessAsync(id, request.GeneralAccess, GetCurrentUserId());
        return Ok(new { message = "General access updated successfully" });
    }

    [HttpPost("folder/{id}/share")]
    public async Task<IActionResult> AddOrUpdateFolderShare(int id, [FromBody] AddUserShareRequest request)
    {
        await _permissionService.AddOrUpdateFolderUserShareAsync(id, request.Email, request.Role, GetCurrentUserId());
        return Ok(new { message = "Folder share updated successfully" });
    }

    [HttpDelete("folder/{id}/share/{targetUserId}")]
    public async Task<IActionResult> RemoveFolderShare(int id, int targetUserId)
    {
        await _permissionService.RemoveFolderUserShareAsync(id, targetUserId, GetCurrentUserId());
        return Ok(new { message = "Folder share removed successfully" });
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AUDIT LOGS (Admin / Moderator)
    // ─────────────────────────────────────────────────────────────────────────

    [HttpGet("/api/admin/audit-logs")]
    [Authorize(Roles = "ADMIN,MODERATOR")]
    public async Task<IActionResult> GetAuditLogs([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var logs = await _permissionService.GetAuditLogsAsync(page, pageSize);
        return Ok(logs);
    }
}
