using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/document")]
public class DocumentController : ControllerBase
{
    private readonly IDocumentService _documentService;
    private readonly IPermissionService _permissionService;
    private readonly IFileStorage _fileStorage;
    private readonly IStudyHubDbContext _db;
    private readonly IDocumentProcessingQueue _queue;
    private readonly IClock _clock;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(
        IDocumentService documentService,
        IPermissionService permissionService,
        IFileStorage fileStorage,
        IStudyHubDbContext db,
        IDocumentProcessingQueue queue,
        IClock clock,
        ILogger<DocumentController> logger)
    {
        _documentService = documentService;
        _permissionService = permissionService;
        _fileStorage = fileStorage;
        _db = db;
        _queue = queue;
        _clock = clock;
        _logger = logger;
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
                return BadRequest(new
                {
                    message = "Vui lòng chọn một file hợp lệ."
                });
            }

            int userId = GetCurrentUserId();
            string originalFileName = file.FileName;
            string fileExtension = Path.GetExtension(originalFileName).TrimStart('.').ToLower();

            using var stream = file.OpenReadStream();
            var docDto = await _documentService.UploadDocumentAsync(userId, folderId, originalFileName, fileExtension, file.Length, stream);
            return Ok(docDto);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Upload argument error: {Message}", ex.Message);
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Upload invalid operation: {Message}", ex.Message);
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Upload unauthorized: {Message}", ex.Message);
            return StatusCode(403, new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception during document upload: {Message}", ex.Message);
            return StatusCode(500, new
            {
                message = $"Lỗi upload: {ex.Message}"
            });
        }
    }

    [HttpPost("confirm")]
    public async Task<IActionResult> Confirm([FromQuery] int documentId, [FromQuery] string title, [FromQuery] string subject, [FromQuery] string sharingPermission, [FromQuery] int? folderId)
    {
        try
        {
            int userId = GetCurrentUserId();
            var doc = await _documentService.ConfirmDocumentAsync(userId, documentId, title, subject, sharingPermission, folderId);
            await _queue.EnqueueJobAsync(doc.DocumentId);
            return Ok(doc);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    [HttpPost("replace")]
    public async Task<IActionResult> Replace([FromQuery] int pendingDocId, [FromQuery] int duplicateDocId, [FromQuery] string title, [FromQuery] string subject, [FromQuery] string sharingPermission, [FromQuery] int? folderId)
    {
        try
        {
            int userId = GetCurrentUserId();
            var doc = await _documentService.ReplaceDocumentAsync(userId, pendingDocId, duplicateDocId, title, subject, sharingPermission, folderId);
            return Ok(doc);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    [HttpPost("keep-both")]
    public async Task<IActionResult> KeepBoth([FromQuery] int pendingDocId, [FromQuery] string title, [FromQuery] string subject, [FromQuery] string sharingPermission, [FromQuery] int? folderId)
    {
        try
        {
            int userId = GetCurrentUserId();
            var doc = await _documentService.KeepBothDocumentsAsync(userId, pendingDocId, title, subject, sharingPermission, folderId);
            return Ok(doc);
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
    }

    [HttpPost("cancel")]
    public async Task<IActionResult> Cancel([FromQuery] int pendingDocId)
    {
        try
        {
            int userId = GetCurrentUserId();
            await _documentService.CancelUploadAsync(userId, pendingDocId);
            return Ok(new
            {
                message = "Hủy bỏ tải lên thành công."
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
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
            return StatusCode(500, new
            {
                message = ex.Message
            });
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
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    [AllowAnonymous]
    [HttpGet("public/paged")]
    public async Task<IActionResult> GetPublicDocumentsPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 12,
        [FromQuery] string? search = null,
        [FromQuery] string? subject = null,
        [FromQuery] string? extensions = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null)
    {
        var extensionList = string.IsNullOrWhiteSpace(extensions)
            ? null
            : extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var paged = await _documentService.GetPublicDocumentsPagedAsync(page, pageSize, search, subject, extensionList, sortBy, sortDirection);
        return Ok(paged);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetDocumentById(int id, [FromQuery] string? token = null)
    {
        var doc = await _documentService.GetDocumentByIdAsync(id);
        if (doc == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy tài liệu."
            });
        }

        int userId = GetCurrentUserId();
        if (!await _permissionService.CanViewDocumentAsync(id, userId, token))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Bạn không có quyền truy cập tài liệu này."
            });
        }

        doc.ViewCount = await _documentService.IncrementViewCountAsync(id, userId);
        return Ok(doc);
    }

    [HttpGet("{id}/version/{versionId}/extracted-text")]
    public async Task<IActionResult> GetExtractedTextByVersion(int id, int versionId, [FromQuery] string? token = null)
    {
        int userId = GetCurrentUserId();
        if (!await _permissionService.CanViewDocumentAsync(id, userId, token))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Bạn không có quyền truy cập tài liệu này."
            });
        }

        var chunks = await _db.DocumentChunks.AsNoTracking()
            .Where(c => c.DocumentId == id && c.DocumentVersionId == versionId)
            .OrderBy(c => c.ChunkIndex)
            .Select(c => new
            {
                chunkId = c.ChunkId,
                chunkIndex = c.ChunkIndex,
                pageNumber = c.PageNumber,
                headingPath = c.HeadingPath,
                startOffset = c.StartOffset,
                endOffset = c.EndOffset,
                text = c.Text
            })
            .ToListAsync();

        if (chunks.Count == 0)
        {
            var isVer1 = await _db.DocumentVersions.AnyAsync(v => v.VersionId == versionId && v.DocumentId == id && v.VersionNumber == 1);
            if (isVer1)
            {
                chunks = await _db.DocumentChunks.AsNoTracking()
                    .Where(c => c.DocumentId == id && c.DocumentVersionId == null)
                    .OrderBy(c => c.ChunkIndex)
                    .Select(c => new
                    {
                        chunkId = c.ChunkId,
                        chunkIndex = c.ChunkIndex,
                        pageNumber = c.PageNumber,
                        headingPath = c.HeadingPath,
                        startOffset = c.StartOffset,
                        endOffset = c.EndOffset,
                        text = c.Text
                    })
                    .ToListAsync();
            }
        }

        string fullText = string.Join("\n\n", chunks.Select(c => c.text));

        return Ok(new
        {
            documentId = id,
            documentVersionId = versionId,
            fullText,
            chunks
        });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteDocument(int id)
    {
        int userId = GetCurrentUserId();
        bool deleted = await _documentService.DeleteDocumentAsync(userId, id);
        if (deleted)
        {
            return Ok(new
            {
                message = "Xóa tài liệu thành công."
            });
        }
        return BadRequest(new
        {
            message = "Không thể xóa tài liệu."
        });
    }

    [HttpGet("{id}/text")]
    public async Task<IActionResult> GetExtractedText(int id, [FromQuery] string? token = null)
    {
        var doc = await _documentService.GetDocumentByIdAsync(id);
        if (doc == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy tài liệu."
            });
        }

        int userId = GetCurrentUserId();
        if (!await _permissionService.CanViewDocumentAsync(id, userId, token))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Bạn không có quyền xem nội dung tài liệu này."
            });
        }

        string? text = await _documentService.GetExtractedTextAsync(id);
        return Ok(new
        {
            documentId = id,
            extractedText = text ?? ""
        });
    }

    [HttpGet("{id}/download")]
    public async Task<IActionResult> DownloadDocument(int id, [FromQuery] string? token = null)
    {
        var doc = await _documentService.GetDocumentByIdAsync(id);
        if (doc == null)
            return NotFound(new
            {
                message = "Không tìm thấy tài liệu."
            });

        int userId = GetCurrentUserId();
        if (!await _permissionService.CanDownloadDocumentAsync(id, userId, token))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Bạn không có quyền tải tài liệu này."
            });

        var relativePath = doc.CloudStorageUrl.TrimStart('/');
        if (!_fileStorage.FileExists(relativePath))
            return NotFound(new
            {
                message = "Không tìm thấy file gốc."
            });

        await _documentService.IncrementDownloadCountAsync(id, userId);
        var fileName = $"{doc.Title}.{doc.FileExtension}";
        var extension = doc.FileExtension.TrimStart('.').ToLowerInvariant();
        var contentType = extension switch
        {
            "pdf" => "application/pdf",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "txt" => "text/plain; charset=utf-8",
            "md" => "text/markdown; charset=utf-8",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
        return PhysicalFile(_fileStorage.GetPhysicalPath(relativePath), contentType, fileName, enableRangeProcessing: true);
    }

    [HttpGet("{id}/raw")]
    public async Task<IActionResult> GetRawFile(int id, [FromQuery] string? token = null)
    {
        var doc = await _documentService.GetDocumentByIdAsync(id);
        if (doc == null)
            return NotFound(new
            {
                message = "Không tìm thấy tài liệu."
            });

        int userId = GetCurrentUserId();
        if (!await _permissionService.CanViewDocumentAsync(id, userId, token))
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                message = "Bạn không có quyền xem tài liệu này."
            });

        var relativePath = doc.CloudStorageUrl.TrimStart('/');
        if (!_fileStorage.FileExists(relativePath))
            return NotFound(new
            {
                message = "Không tìm thấy file gốc."
            });

        var extension = doc.FileExtension.TrimStart('.').ToLowerInvariant();
        var contentType = extension switch
        {
            "pdf" => "application/pdf",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "txt" => "text/plain; charset=utf-8",
            "md" => "text/markdown; charset=utf-8",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
        return PhysicalFile(_fileStorage.GetPhysicalPath(relativePath), contentType, enableRangeProcessing: true);
    }

    [AllowAnonymous]
    [HttpGet("shared/{token}")]
    public async Task<IActionResult> GetSharedDocumentByToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Mã liên kết không hợp lệ." });

        var docEntity = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.ShareLinkToken == token && !d.IsDeleted);
        if (docEntity == null)
            return NotFound(new { message = "Không tìm thấy tài liệu chia sẻ hoặc liên kết đã bị xóa." });

        if (docEntity.GeneralAccess != "LINK" || docEntity.IsShareLinkRevoked || (docEntity.ShareLinkExpiresAt.HasValue && docEntity.ShareLinkExpiresAt.Value < DateTime.UtcNow))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Liên kết chia sẻ này đã hết hạn hoặc bị thu hồi quyền truy cập." });
        }

        var doc = await _documentService.GetDocumentByIdAsync(docEntity.DocumentId);
        if (doc == null)
            return NotFound(new { message = "Không tìm thấy tài liệu." });

        int userId = GetCurrentUserId();
        doc.ViewCount = await _documentService.IncrementViewCountAsync(doc.DocumentId, userId);
        return Ok(doc);
    }

    [AllowAnonymous]
    [HttpGet("shared/{token}/text")]
    public async Task<IActionResult> GetSharedExtractedTextByToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Mã liên kết không hợp lệ." });

        var docEntity = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.ShareLinkToken == token && !d.IsDeleted);
        if (docEntity == null || docEntity.GeneralAccess != "LINK" || docEntity.IsShareLinkRevoked || (docEntity.ShareLinkExpiresAt.HasValue && docEntity.ShareLinkExpiresAt.Value < DateTime.UtcNow))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Không có quyền truy cập nội dung tài liệu này." });
        }

        string? text = await _documentService.GetExtractedTextAsync(docEntity.DocumentId);
        return Ok(new
        {
            documentId = docEntity.DocumentId,
            extractedText = text ?? ""
        });
    }

    [AllowAnonymous]
    [HttpGet("shared/{token}/raw")]
    public async Task<IActionResult> GetSharedRawFileByToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return BadRequest(new { message = "Mã liên kết không hợp lệ." });

        var docEntity = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.ShareLinkToken == token && !d.IsDeleted);
        if (docEntity == null || docEntity.GeneralAccess != "LINK" || docEntity.IsShareLinkRevoked || (docEntity.ShareLinkExpiresAt.HasValue && docEntity.ShareLinkExpiresAt.Value < DateTime.UtcNow))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Không có quyền truy cập tệp tài liệu này." });
        }

        var relativePath = docEntity.CloudStorageUrl.TrimStart('/');
        if (!_fileStorage.FileExists(relativePath))
            return NotFound(new { message = "Không tìm thấy file gốc trên máy chủ." });

        var extension = docEntity.FileExtension.TrimStart('.').ToLowerInvariant();
        var contentType = extension switch
        {
            "pdf" => "application/pdf",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            "txt" => "text/plain; charset=utf-8",
            "md" => "text/markdown; charset=utf-8",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            _ => "application/octet-stream"
        };
        return PhysicalFile(_fileStorage.GetPhysicalPath(relativePath), contentType, enableRangeProcessing: true);
    }

    [HttpGet("analytics")]
    public async Task<IActionResult> GetAnalytics() => Ok(await _documentService.GetUserAnalyticsAsync(GetCurrentUserId()));

    [HttpGet("shared-with-me")]
    public async Task<IActionResult> SharedWithMe()
    {
        var userId = GetCurrentUserId();
        var sharedDocIds = await _permissionService.GetSharedDocumentIdsAsync(userId);
        var result = new List<DocumentResponseDto>();
        foreach (var id in sharedDocIds)
        {
            var document = await _documentService.GetDocumentByIdAsync(id);
            if (document != null && document.UserId != userId)
            {
                result.Add(document);
            }
        }
        return Ok(result);
    }

    [HttpGet("{id}/shares")]
    public async Task<IActionResult> Shares(int id)
    {
        var userId = GetCurrentUserId();
        if (!await _permissionService.CanManageDocumentAccessAsync(id, userId)) return Forbid();
        return Ok(await _db.DocumentShares.AsNoTracking().Where(s => s.DocumentId == id)
            .Select(s => new { s.SharedWithUserId, s.SharedWithUser.Username, s.SharedWithUser.Email, s.CreatedAt }).ToListAsync());
    }

    [HttpPost("{id}/shares/{friendUserId}")]
    public async Task<IActionResult> ShareWithFriend(int id, int friendUserId)
    {
        var userId = GetCurrentUserId();
        if (!await _permissionService.CanManageDocumentAccessAsync(id, userId)) return Forbid();
        var document = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == id);
        if (document == null) return NotFound(new { message = "Không tìm thấy tài liệu." });
        var areFriends = await _db.Friendships.AnyAsync(f => f.Status == "ACCEPTED" &&
            ((f.RequesterId == userId && f.AddresseeId == friendUserId) || (f.RequesterId == friendUserId && f.AddresseeId == userId)));
        if (!areFriends) return BadRequest(new { message = "Chỉ có thể chia sẻ tài liệu cho bạn bè." });
        if (!await _db.DocumentShares.AnyAsync(s => s.DocumentId == id && s.SharedWithUserId == friendUserId))
        {
            _db.DocumentShares.Add(new DocumentShare { DocumentId = id, OwnerUserId = document.UserId, SharedWithUserId = friendUserId, CreatedAt = DateTime.Now });
            _db.ModerationNotices.Add(new ModerationNotice { UserId = friendUserId, DocumentId = id, RelatedUserId = userId,
                Type = "DOCUMENT_SHARED", Title = "Bạn nhận được một tài liệu", Message = $"Một người bạn đã chia sẻ tài liệu “{document.Title}” với bạn.",
                ActionUrl = $"/document/{id}", IsRead = false, CreatedAt = DateTime.Now });
            await _db.SaveChangesAsync();
        }
        return Ok(new { message = "Đã chia sẻ tài liệu." });
    }

    [HttpDelete("{id}/shares/{friendUserId}")]
    public async Task<IActionResult> RemoveShare(int id, int friendUserId)
    {
        var userId = GetCurrentUserId();
        if (!await _permissionService.CanManageDocumentAccessAsync(id, userId)) return Forbid();
        var share = await _db.DocumentShares.FirstOrDefaultAsync(s => s.DocumentId == id && s.SharedWithUserId == friendUserId);
        if (share == null) return NotFound();
        _db.DocumentShares.Remove(share);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("{id}/audience")]
    public async Task<IActionResult> GetAudience(int id)
    {
        var detail = await _documentService.GetDocumentDetailAsync(id);
        if (detail == null)
            return NotFound();
        var userId = GetCurrentUserId();
        return await _permissionService.CanManageDocumentAccessAsync(id, userId) ? Ok(detail) : Forbid();
    }

    [HttpPost("report")]
    public async Task<IActionResult> ReportDocument([FromBody] DocumentReportDto reportDto)
    {
        int userId = GetCurrentUserId();
        bool success = await _documentService.ReportDocumentAsync(userId, reportDto);
        if (success)
        {
            return Ok(new
            {
                message = "Báo cáo tài liệu thành công. Đội ngũ admin sẽ xem xét."
            });
        }
        return BadRequest(new
        {
            message = "Không thể gửi báo cáo tài liệu."
        });
    }

    [HttpPost("reports/{reportId}/appeal")]
    public async Task<IActionResult> Appeal(int reportId, [FromBody] AppealDto dto)
    {
        var userId = GetCurrentUserId();
        var report = await _db.DocumentReports.Include(r => r.Document).FirstOrDefaultAsync(r => r.ReportId == reportId && r.Document.UserId == userId);
        if (report == null || report.Status is not ("VIOLATION_CONFIRMED" or "ACTION_TAKEN" or "ACTIONED") || string.IsNullOrWhiteSpace(dto.Explanation) || await _db.ModerationAppeals.AnyAsync(a => a.ReportId == reportId))
            return BadRequest(new
            {
                message = "Không thể gửi giải trình."
            });

        if (!report.ResolvedAt.HasValue || _clock.UtcNow > report.ResolvedAt.Value.AddDays(14))
        {
            return BadRequest(new
            {
                message = "Đã hết thời hạn khiếu nại (14 ngày kể từ khi quyết định xử lý được ban hành)."
            });
        }

        var appeal = new ModerationAppeal { ReportId = reportId, SubmittedByUserId = userId, Explanation = dto.Explanation.Trim(), EvidenceUrl = dto.EvidenceUrl?.Trim(), Status = "PENDING", CreatedAt = _clock.UtcNow };
        _db.ModerationAppeals.Add(appeal);
        report.Status = "APPEALED";
        await _db.SaveChangesAsync();
        var moderators = await _db.Users.Where(u => u.Role == "MODERATOR" && u.Status == "ACTIVE").Select(u => u.UserId).ToListAsync();
        _db.ModerationNotices.AddRange(moderators.Select(moderatorId => new ModerationNotice
        {
            UserId = moderatorId,
            DocumentId = report.DocumentId,
            ReportId = reportId,
            Type = "APPEAL_PENDING",
            Title = "Có giải trình mới",
            Message = $"Người đăng vừa gửi giải trình cho tài liệu “{report.Document.Title}”.",
            ActionUrl = $"/moderator?tab=appeals&appealId={appeal.AppealId}",
            IsRead = false,
            CreatedAt = _clock.UtcNow
        }));
        await _db.SaveChangesAsync();
        return Ok(new
        {
            message = "Đã gửi giải trình."
        });
    }

    [HttpGet("{documentId}/appealable-report")]
    public async Task<IActionResult> AppealableReport(int documentId)
    {
        var userId = GetCurrentUserId();
        var fourteenDaysAgo = _clock.UtcNow.AddDays(-14);
        var reportId = await _db.DocumentReports.AsNoTracking().Where(r => r.DocumentId == documentId && r.Document.UserId == userId &&
            (r.Status == "VIOLATION_CONFIRMED" || r.Status == "ACTION_TAKEN" || r.Status == "ACTIONED") &&
            r.ResolvedAt.HasValue && r.ResolvedAt.Value >= fourteenDaysAgo)
            .OrderByDescending(r => r.ResolvedAt).Select(r => (int?)r.ReportId).FirstOrDefaultAsync();
        return Ok(new
        {
            reportId
        });
    }

    [HttpGet("moderation-notices")]
    public async Task<IActionResult> ModerationNotices([FromQuery] bool unreadOnly = false)
    {
        var userId = GetCurrentUserId();
        var query = _db.ModerationNotices.AsNoTracking().Where(n => n.UserId == userId);
        if (unreadOnly)
            query = query.Where(n => !n.IsRead);
        return Ok(await query.OrderByDescending(n => n.CreatedAt).Select(n => new
        {
            n.NoticeId,
            n.DocumentId,
            n.ReportId,
            n.TransactionId,
            n.RelatedUserId,
            n.ActionUrl,
            n.Type,
            n.Title,
            n.Message,
            n.CanAppeal,
            n.IsRead,
            n.CreatedAt
        }).ToListAsync());
    }

    [HttpPost("moderation-notices/{noticeId}/read")]
    public async Task<IActionResult> ReadModerationNotice(long noticeId)
    {
        var userId = GetCurrentUserId();
        var notice = await _db.ModerationNotices.FirstOrDefaultAsync(n => n.NoticeId == noticeId && n.UserId == userId);
        if (notice == null)
            return NotFound();
        notice.IsRead = true;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("moderation-notices/read-all")]
    public async Task<IActionResult> ReadAllModerationNotices()
    {
        var userId = GetCurrentUserId();
        await _db.ModerationNotices.Where(n => n.UserId == userId && !n.IsRead).ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        return NoContent();
    }

    [HttpDelete("moderation-notices/{noticeId}")]
    public async Task<IActionResult> DeleteModerationNotice(long noticeId)
    {
        var userId = GetCurrentUserId();
        var notice = await _db.ModerationNotices.FirstOrDefaultAsync(n => n.NoticeId == noticeId && n.UserId == userId);
        if (notice == null)
            return NotFound();
        _db.ModerationNotices.Remove(notice);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpGet("report-reasons")]
    public async Task<IActionResult> GetReportReasons() => Ok(await _documentService.GetReportReasonsAsync());

    [HttpGet("bookmarks")]
    public async Task<IActionResult> GetBookmarks()
    {
        var documentIds = await _documentService.GetBookmarkedDocumentIdsAsync(GetCurrentUserId());
        return Ok(documentIds);
    }

    [HttpPost("{id}/bookmark")]
    public async Task<IActionResult> AddBookmark(int id)
    {
        var count = await _documentService.AddBookmarkAsync(GetCurrentUserId(), id);
        return count.HasValue
            ? Ok(new
            {
                documentId = id,
                bookmarkCount = count.Value,
                isBookmarked = true
            })
            : NotFound(new
            {
                message = "Không tìm thấy tài liệu công khai."
            });
    }

    [HttpDelete("{id}/bookmark")]
    public async Task<IActionResult> RemoveBookmark(int id)
    {
        var count = await _documentService.RemoveBookmarkAsync(GetCurrentUserId(), id);
        return count.HasValue
            ? Ok(new
            {
                documentId = id,
                bookmarkCount = count.Value,
                isBookmarked = false
            })
            : NotFound(new
            {
                message = "Không tìm thấy tài liệu."
            });
    }

    [HttpPost("{id}/retry-extraction")]
    public async Task<IActionResult> RetryExtraction(int id)
    {
        await _documentService.RetryExtractionAsync(id, GetCurrentUserId());
        return Ok(new { message = "Extraction retry initiated" });
    }

    [HttpPut("{id}/metadata")]
    public async Task<IActionResult> UpdateMetadata(int id, [FromBody] UpdateDocumentMetadataRequest request)
    {
        var userId = GetCurrentUserId();
        if (!await _permissionService.CanManageDocumentAccessAsync(id, userId))
            return Forbid();

        var document = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == id && !d.IsDeleted);
        if (document == null)
            return NotFound(new { message = "Không tìm thấy tài liệu." });

        var title = (request.Title ?? string.Empty).Trim();
        var extensionSuffix = $".{document.FileExtension}";
        if (title.EndsWith(extensionSuffix, StringComparison.OrdinalIgnoreCase))
            title = title[..^extensionSuffix.Length].Trim();

        if (string.IsNullOrWhiteSpace(title) || title.Length > 255)
            return BadRequest(new { message = "Tên tài liệu phải có từ 1 đến 255 ký tự." });

        var subject = (request.Subject ?? string.Empty).Trim();
        var resolvedSubject = await _db.SubjectCategories.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == subject && (s.Status == "APPROVED" || s.Status == "PENDING"));
        if (resolvedSubject == null)
            return BadRequest(new { message = "Môn học hoặc chuyên mục đã chọn không hợp lệ." });

        var duplicateTitle = await _db.Documents.AsNoTracking().AnyAsync(d =>
            d.DocumentId != id && d.UserId == document.UserId && d.FolderId == document.FolderId &&
            !d.IsDeleted && d.Title == title && d.FileExtension == document.FileExtension);
        if (duplicateTitle)
            return Conflict(new { message = "Trong thư mục này đã có tài liệu cùng tên và định dạng." });

        document.Title = title;
        document.Subject = resolvedSubject.Name;
        await _db.SaveChangesAsync();

        return Ok(await _documentService.GetDocumentByIdAsync(id));
    }

    [HttpGet("storage-quota")]
    public async Task<IActionResult> GetStorageQuota()
    {
        var quota = await _documentService.GetUserStorageQuotaAsync(GetCurrentUserId());
        return Ok(quota);
    }

    [HttpGet("my-documents/paged")]
    public async Task<IActionResult> GetMyDocumentsPaged([FromQuery] int? folderId, [FromQuery] int page = 1, [FromQuery] int pageSize = 12, [FromQuery] string? search = null, [FromQuery] string? subject = null)
    {
        var paged = await _documentService.GetMyDocumentsPagedAsync(GetCurrentUserId(), folderId, page, pageSize, search, subject);
        return Ok(paged);
    }

    [HttpGet("shared-with-me/paged")]
    public async Task<IActionResult> GetSharedWithMePaged([FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        var paged = await _documentService.GetSharedWithMePagedAsync(GetCurrentUserId(), page, pageSize);
        return Ok(paged);
    }

    [HttpGet("bookmarks/paged")]
    public async Task<IActionResult> GetBookmarksPaged([FromQuery] int page = 1, [FromQuery] int pageSize = 12)
    {
        var paged = await _documentService.GetBookmarksPagedAsync(GetCurrentUserId(), page, pageSize);
        return Ok(paged);
    }

    [HttpPost("bulk-delete")]
    public async Task<IActionResult> BulkDelete([FromBody] List<int> documentIds)
    {
        await _documentService.BulkDeleteDocumentsAsync(documentIds, GetCurrentUserId());
        return Ok(new { message = "Bulk delete successful" });
    }

    [HttpPost("bulk-move")]
    public async Task<IActionResult> BulkMove([FromBody] BulkMoveRequest request)
    {
        await _documentService.BulkMoveDocumentsAsync(request.DocumentIds, request.TargetFolderId, GetCurrentUserId());
        return Ok(new { message = "Bulk move successful" });
    }
}

public class BulkMoveRequest
{
    public List<int> DocumentIds { get; set; } = new();
    public int? TargetFolderId { get; set; }
}

public class UpdateDocumentMetadataRequest
{
    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
}

public class AppealDto
{
    public string Explanation { get; set; } = null!; public string? EvidenceUrl
    {
        get; set;
    }
}
