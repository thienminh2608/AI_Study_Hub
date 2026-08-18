using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIStudyHub.Api.Controllers;

[ApiController]
[Route("api/documents/{documentId}/versions")]
[Authorize]
public class DocumentVersionController : ControllerBase
{
    private readonly IVersionService _versionService;

    public DocumentVersionController(IVersionService versionService)
    {
        _versionService = versionService;
    }

    private int GetCurrentUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        return claim != null ? int.Parse(claim.Value) : 0;
    }

    [HttpGet]
    public async Task<IActionResult> GetVersionHistory(int documentId)
    {
        var versions = await _versionService.GetVersionHistoryAsync(documentId, GetCurrentUserId());
        return Ok(versions);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadNewVersion(int documentId, [FromForm] UploadVersionRequest request)
    {
        if (request.File == null || request.File.Length == 0)
            return BadRequest("File cannot be empty");

        using var stream = request.File.OpenReadStream();
        var newVersion = await _versionService.CreateNewVersionAsync(documentId, stream, request.File.FileName, request.ChangeSummary, GetCurrentUserId());
        return Ok(newVersion);
    }

    [HttpPost("{versionId}/restore")]
    public async Task<IActionResult> RestoreVersion(int documentId, int versionId)
    {
        await _versionService.RestoreVersionAsync(documentId, versionId, GetCurrentUserId());
        return Ok(new { message = "Version restored successfully" });
    }
}

public class UploadVersionRequest
{
    public IFormFile File { get; set; } = null!;
    public string? ChangeSummary { get; set; }
}
