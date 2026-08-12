using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AIStudyHub.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/document")]
public class DocumentController : ControllerBase
{
    private readonly IDocumentService _documentService;

    public DocumentController(IDocumentService documentService)
    {
        _documentService = documentService;
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

    [HttpPost("upload")]
    public async Task<IActionResult> Upload(IFormFile file, [FromQuery] int? folderId)
    {
        try
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { message = "Vui lòng chọn một file hợp lệ." });
            }

            int userId = GetCurrentUserId();
            string originalFileName = file.FileName;
            string fileExtension = Path.GetExtension(originalFileName).TrimStart('.').ToLower();

            using var stream = file.OpenReadStream();
            var docDto = await _documentService.UploadDocumentAsync(userId, folderId, originalFileName, fileExtension, file.Length, stream);
            return Ok(docDto);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = $"Lỗi upload: {ex.Message}" });
        }
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromQuery] int documentId, [FromQuery] string title, [FromQuery] string sharingPermission, [FromQuery] int? folderId)
    {
        try
        {
            int userId = GetCurrentUserId();
            var doc = await _documentService.ConfirmDocumentAsync(userId, documentId, title, sharingPermission, folderId);
            return Ok(doc);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("replace")]
    public async Task<IActionResult> Replace([FromQuery] int pendingDocId, [FromQuery] int duplicateDocId, [FromQuery] string title, [FromQuery] string sharingPermission, [FromQuery] int? folderId)
    {
        try
        {
            int userId = GetCurrentUserId();
            var doc = await _documentService.ReplaceDocumentAsync(userId, pendingDocId, duplicateDocId, title, sharingPermission, folderId);
            return Ok(doc);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("keep-both")]
    public async Task<IActionResult> KeepBoth([FromQuery] int pendingDocId, [FromQuery] string title, [FromQuery] string sharingPermission, [FromQuery] int? folderId)
    {
        try
        {
            int userId = GetCurrentUserId();
            var doc = await _documentService.KeepBothDocumentsAsync(userId, pendingDocId, title, sharingPermission, folderId);
            return Ok(doc);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel([FromQuery] int pendingDocId)
    {
        try
        {
            int userId = GetCurrentUserId();
            await _documentService.CancelUploadAsync(userId, pendingDocId);
            return Ok(new { message = "Hủy bỏ tải lên thành công." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetUserDocuments([FromQuery] int? folderId)
    {
        try
        {
            int userId = GetCurrentUserId();
            var docs = await _documentService.GetUserDocumentsAsync(userId, folderId);
            return Ok(docs);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [AllowAnonymous]
    [HttpGet("public")]
    public async Task<IActionResult> GetPublicDocuments()
    {
        try
        {
            var docs = await _documentService.GetPublicDocumentsAsync();
            return Ok(docs);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDocumentById(int id)
    {
        var doc = await _documentService.GetDocumentByIdAsync(id);
        if (doc == null)
        {
            return NotFound(new { message = "Không tìm thấy tài liệu." });
        }

        // Access check
        int userId = GetCurrentUserId();
        if (doc.UserId != userId && doc.SharingPermission != "PUBLIC")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Bạn không có quyền truy cập tài liệu này." });
        }

        return Ok(doc);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDocument(int id)
    {
        int userId = GetCurrentUserId();
        bool deleted = await _documentService.DeleteDocumentAsync(userId, id);
        if (deleted)
        {
            return Ok(new { message = "Xóa tài liệu thành công." });
        }
        return BadRequest(new { message = "Không thể xóa tài liệu." });
    }

    [HttpGet("{id}/text")]
    public async Task<IActionResult> GetExtractedText(int id)
    {
        var doc = await _documentService.GetDocumentByIdAsync(id);
        if (doc == null)
        {
            return NotFound(new { message = "Không tìm thấy tài liệu." });
        }

        int userId = GetCurrentUserId();
        if (doc.UserId != userId && doc.SharingPermission != "PUBLIC")
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Bạn không có quyền xem nội dung tài liệu này." });
        }

        string? text = await _documentService.GetExtractedTextAsync(id);
        return Ok(new { documentId = id, extractedText = text ?? "" });
    }

    [HttpPost("report")]
    public async Task<IActionResult> ReportDocument([FromBody] DocumentReportDto reportDto)
    {
        int userId = GetCurrentUserId();
        bool success = await _documentService.ReportDocumentAsync(userId, reportDto);
        if (success)
        {
            return Ok(new { message = "Báo cáo tài liệu thành công. Đội ngũ admin sẽ xem xét." });
        }
        return BadRequest(new { message = "Không thể gửi báo cáo tài liệu." });
    }
}
