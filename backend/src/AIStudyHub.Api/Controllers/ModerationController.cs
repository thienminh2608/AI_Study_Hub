using System;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Api.Controllers;

[Authorize(Roles = "ADMIN,MODERATOR")]
[ApiController]
[Route("api/moderation")]
public class ModerationController : ControllerBase
{
    private readonly IStudyHubDbContext _db;
    private readonly IDocumentService _documents;
    private readonly IFileStorage _fileStorage;
    private readonly IClock _clock;

    public ModerationController(
        IStudyHubDbContext db,
        IDocumentService documents,
        IFileStorage fileStorage,
        IClock clock)
    {
        _db = db;
        _documents = documents;
        _fileStorage = fileStorage;
        _clock = clock;
    }

    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("summary")]
    public async Task<IActionResult> Summary() => Ok(new
    {
        PendingDocuments = await _db.Documents.CountAsync(d => d.ModerationStatus == "PENDING_REVIEW" || d.ModerationStatus == "IN_REVIEW"),
        PendingReports = await _db.DocumentReports.CountAsync(r => r.Status == "PENDING" || r.Status == "IN_REVIEW" || r.Status == "RESTRICTED"),
        PendingAppeals = await _db.ModerationAppeals.CountAsync(a => a.Status == "PENDING"),
        Completed = await _db.ModerationActions.CountAsync()
    });

    [HttpGet("queue")]
    public async Task<IActionResult> Queue() => Ok(await _db.Documents.AsNoTracking().Include(d => d.User)
        .Where(d => d.ModerationStatus == "PENDING_REVIEW" || d.ModerationStatus == "IN_REVIEW")
        .OrderBy(d => d.ModerationSubmittedAt).Select(d => new
        {
            d.DocumentId,
            d.Title,
            d.Subject,
            d.FileExtension,
            d.FileSizeMb,
            d.ModerationStatus,
            d.ModerationSubmittedAt,
            d.UserId,
            UploaderName = d.User.Username
        }).ToListAsync());

    [HttpGet("queue/paged")]
    public async Task<IActionResult> QueuePaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Documents.AsNoTracking()
            .Include(d => d.User)
            .Where(d => d.ModerationStatus == "PENDING_REVIEW" || d.ModerationStatus == "IN_REVIEW");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim();
            query = query.Where(d => d.Title.Contains(s) || (d.Subject != null && d.Subject.Contains(s)) || d.User.Username.Contains(s));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderBy(d => d.ModerationSubmittedAt)
            .ThenBy(d => d.DocumentId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(d => new
            {
                d.DocumentId,
                d.Title,
                d.Subject,
                d.FileExtension,
                d.FileSizeMb,
                d.ModerationStatus,
                d.ModerationSubmittedAt,
                d.UserId,
                UploaderName = d.User.Username
            })
            .ToListAsync();

        return Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        });
    }

    [HttpGet("documents/{id}")]
    public async Task<IActionResult> DocumentDetail(int id)
    {
        var doc = await _db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.DocumentId == id);
        if (doc == null || doc.ModerationStatus == "NOT_REQUESTED")
            return NotFound();
        return Ok(await _documents.GetDocumentDetailAsync(id));
    }

    [HttpPost("documents/{id}/{decision}")]
    public async Task<IActionResult> ReviewDocument(int id, string decision, [FromBody] ModerationDecisionDto dto)
    {
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.DocumentId == id);
        if (doc == null || doc.ModerationStatus is not ("PENDING_REVIEW" or "IN_REVIEW" or "NEEDS_CHANGES"))
            return BadRequest(new
            {
                message = "Tài liệu không ở trạng thái có thể xét duyệt."
            });
        var previous = doc.ModerationStatus;
        var normalized = decision.Trim().ToUpperInvariant();
        if (normalized == "APPROVE")
        {
            doc.ModerationStatus = "APPROVED";
            doc.SharingPermission = "PUBLIC";
            doc.IsFlagged = false;
        }
        else if (normalized == "REJECT")
        {
            if (string.IsNullOrWhiteSpace(dto.Note))
                return BadRequest(new
                {
                    message = "Cần nhập lý do từ chối."
                });
            doc.ModerationStatus = "REJECTED";
            doc.SharingPermission = "PRIVATE";
        }
        else if (normalized == "REQUEST-CHANGES")
        {
            if (string.IsNullOrWhiteSpace(dto.Note))
                return BadRequest(new
                {
                    message = "Cần nhập nội dung cần chỉnh sửa."
                });
            doc.ModerationStatus = "NEEDS_CHANGES";
            doc.SharingPermission = "PRIVATE";
        }
        else
            return BadRequest(new
            {
                message = "Hành động không hợp lệ."
            });
        doc.ModeratedByUserId = ActorId;
        doc.ModeratedAt = _clock.UtcNow;
        doc.ModerationNote = dto.Note?.Trim();
        if (normalized is "REJECT" or "REQUEST-CHANGES")
            _db.ModerationNotices.Add(Notice(doc.UserId, doc.DocumentId, null, normalized, doc.Title,
                normalized == "REJECT" ? $"Tài liệu đã bị từ chối công khai. Lý do: {dto.Note}" : $"Tài liệu cần được chỉnh sửa trước khi gửi duyệt lại. Nội dung: {dto.Note}", false));
        else if (normalized == "APPROVE")
            _db.ModerationNotices.Add(Notice(doc.UserId, doc.DocumentId, null, "DOCUMENT_APPROVED", "Tài liệu đã được xét duyệt",
                $"Tài liệu “{doc.Title}” đã được xét duyệt thành công và hiện được công khai.", false));

        _db.ModerationActions.Add(Action(id, null, normalized, previous, doc.ModerationStatus, dto.Note));
        await _db.SaveChangesAsync();
        return Ok(new
        {
            doc.DocumentId,
            doc.ModerationStatus,
            doc.SharingPermission
        });
    }

    [HttpGet("reports")]
    public async Task<IActionResult> Reports() => Ok(await _documents.GetReportsAsync());

    [HttpGet("reports/paged")]
    public async Task<IActionResult> ReportsPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] string? search = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.DocumentReports.AsNoTracking()
            .Include(r => r.Document)
            .Include(r => r.Reporter)
            .Include(r => r.AssignedModerator)
            .Include(r => r.ReportedVersion)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToUpperInvariant();
            query = query.Where(r => r.Status == s);
        }

        if (!string.IsNullOrWhiteSpace(type))
        {
            var t = type.Trim().ToUpperInvariant();
            query = query.Where(r => r.ReportType == t);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r => r.Document.Title.Contains(term) || (r.Reporter != null && r.Reporter.Username.Contains(term)) || (r.ClaimantName != null && r.ClaimantName.Contains(term)));
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.ReportId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.ReportId,
                r.DocumentId,
                DocumentTitle = r.Document.Title,
                r.ReportType,
                r.ReasonCode,
                r.AdditionalDetails,
                r.ClaimantName,
                r.ClaimantEmail,
                r.OriginalWorkUrl,
                r.EvidenceDescription,
                r.Status,
                r.ReportedVersionId,
                ReportedVersionNumber = r.ReportedVersion != null ? r.ReportedVersion.VersionNumber : 1,
                r.AssignedModeratorId,
                AssignedModeratorName = r.AssignedModerator != null ? r.AssignedModerator.Username : null,
                r.ModeratorNote,
                r.RestrictedAt,
                r.ResolvedAt,
                r.CreatedAt,
                ReporterName = r.Reporter != null ? r.Reporter.Username : r.ClaimantName
            })
            .ToListAsync();

        return Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        });
    }

    [HttpPost("reports/{id}/assign")]
    public async Task<IActionResult> Assign(int id)
    {
        try
        {
            if (!User.IsInRole("ADMIN") && await _db.DocumentReports
                    .AnyAsync(report => report.ReportId == id && report.ReporterId == ActorId))
            {
                return Conflict(new
                {
                    message = "Bạn không thể nhận xử lý báo cáo do chính mình tạo."
                });
            }

            using var tx = await _db.Database.BeginTransactionAsync();
            var rowsAffected = await _db.Database.ExecuteSqlInterpolatedAsync($@"
                UPDATE document_reports
                SET assigned_moderator_id = {ActorId}, status = 'IN_REVIEW'
                WHERE report_id = {id} AND status IN ('PENDING', 'IN_REVIEW') AND (assigned_moderator_id IS NULL OR assigned_moderator_id = {ActorId})");

            if (rowsAffected == 0)
            {
                await tx.RollbackAsync();
                return Conflict(new { message = "Báo cáo này đã được tiếp nhận bởi kiểm duyệt viên khác hoặc không ở trạng thái chờ xử lý." });
            }

            var report = await _db.DocumentReports.FindAsync(id);
            if (report != null)
            {
                _db.ModerationActions.Add(Action(report.DocumentId, id, "ASSIGN", "PENDING", "IN_REVIEW", null));
                await _db.SaveChangesAsync();
            }
            await tx.CommitAsync();
            return Ok(new { message = "Đã nhận xử lý báo cáo thành công." });
        }
        catch (Exception)
        {
            return Conflict(new { message = "Báo cáo này đã được tiếp nhận bởi kiểm duyệt viên khác hoặc xảy ra tranh chấp dữ liệu." });
        }
    }

    [HttpPost("reports/{id}/{decision}")]
    public async Task<IActionResult> Resolve(int id, string decision, [FromBody] ModerationDecisionDto dto)
    {
        var report = await _db.DocumentReports.Include(r => r.Document).FirstOrDefaultAsync(r => r.ReportId == id);
        if (report == null || report.Status is not ("IN_REVIEW" or "RESTRICTED"))
            return BadRequest(new
            {
                message = "Báo cáo phải được nhận xử lý trước khi đưa ra quyết định."
            });
        if (!User.IsInRole("ADMIN") && report.ReporterId == ActorId)
            return Forbid();
        if (!User.IsInRole("ADMIN") && report.AssignedModeratorId != ActorId)
            return Forbid();

        var previous = report.Status ?? "PENDING";
        var normalized = decision.ToUpperInvariant();
        if (normalized == "TEMPORARILY-HIDE")
        {
            if (previous == "RESTRICTED")
                return BadRequest(new
                {
                    message = "Tài liệu đã được ẩn tạm thời."
                });
            if (string.IsNullOrWhiteSpace(dto.Note))
                return BadRequest(new
                {
                    message = "Cần nhập căn cứ ẩn tạm thời."
                });
            report.PreviousSharingPermission = report.Document.SharingPermission;
            report.RestrictedAt = _clock.UtcNow;
            report.Status = "RESTRICTED";
            report.Document.SharingPermission = "PRIVATE";
            report.Document.IsFlagged = true;
            _db.ModerationNotices.Add(Notice(report.Document.UserId, report.DocumentId, report.ReportId, "TEMPORARILY_HIDDEN", report.Document.Title,
                $"Tài liệu đã được ẩn tạm thời trong khi Moderator xác minh báo cáo. Căn cứ: {dto.Note}", false));
        }
        else if (normalized == "NO-VIOLATION")
        {
            if (string.IsNullOrWhiteSpace(dto.Note))
                return BadRequest(new
                {
                    message = "Cần ghi kết luận bác báo cáo."
                });
            report.Status = "NO_VIOLATION";
            if (previous == "RESTRICTED")
                report.Document.SharingPermission = report.PreviousSharingPermission ?? "PUBLIC";
            report.Document.IsFlagged = false;
            report.ResolvedByAdminId = ActorId;
            report.ResolvedAt = _clock.UtcNow;
        }
        else if (normalized == "CONFIRM-VIOLATION")
        {
            if (string.IsNullOrWhiteSpace(dto.Note))
                return BadRequest(new
                {
                    message = "Cần ghi căn cứ xác nhận vi phạm."
                });
            report.Status = "VIOLATION_CONFIRMED";
            report.Document.SharingPermission = "PRIVATE";
            report.Document.ModerationStatus = "HIDDEN";
            report.Document.IsFlagged = true;
            report.ResolvedByAdminId = ActorId;
            report.ResolvedAt = _clock.UtcNow;
            _db.ModerationNotices.Add(Notice(report.Document.UserId, report.DocumentId, report.ReportId, "VIOLATION_CONFIRMED", report.Document.Title,
                $"Moderator đã xác nhận tài liệu vi phạm và gỡ khỏi khu vực công khai. Bạn có quyền gửi giải trình trong vòng 14 ngày. Căn cứ: {dto.Note}", true));
        }
        else
            return BadRequest(new
            {
                message = "Hành động xử lý báo cáo không hợp lệ."
            });

        report.ModeratorNote = dto.Note?.Trim();
        _db.ModerationActions.Add(Action(report.DocumentId, id, normalized, previous, report.Status, dto.Note));
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("reports/{reportId}/evidence/raw")]
    public async Task<IActionResult> GetReportRawEvidence(int reportId)
    {
        var report = await _db.DocumentReports
            .Include(r => r.Document)
            .Include(r => r.ReportedVersion)
            .FirstOrDefaultAsync(r => r.ReportId == reportId);

        if (report == null)
            return NotFound(new { message = "Không tìm thấy báo cáo vi phạm." });

        string? cloudUrl = report.ReportedVersion?.CloudStorageUrl ?? report.Document.CloudStorageUrl;
        string? extension = report.ReportedVersion?.FileExtension ?? report.Document.FileExtension;

        if (string.IsNullOrWhiteSpace(cloudUrl))
            return NotFound(new { message = "Tài liệu không có đường dẫn tệp hợp lệ." });

        string relativePath = cloudUrl.TrimStart('/');
        if (!_fileStorage.FileExists(relativePath))
        {
            return NotFound(new { message = "Tệp bằng chứng vật lý không tồn tại trên hệ thống lưu trữ." });
        }

        string physicalPath = _fileStorage.GetPhysicalPath(relativePath);
        string contentType = extension?.ToLowerInvariant() switch
        {
            "pdf" => "application/pdf",
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "gif" => "image/gif",
            "txt" => "text/plain",
            "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            _ => "application/octet-stream"
        };

        string safeFileName = Regex.Replace(report.Document.Title ?? "evidence", @"[^a-zA-Z0-9.\-_]", "_");
        int verNumber = report.ReportedVersion?.VersionNumber ?? 1;
        string downloadFileName = $"evidence_report_{reportId}_v{verNumber}_{safeFileName}.{extension}";

        var stream = _fileStorage.OpenReadStream(relativePath);
        return File(stream, contentType, downloadFileName);
    }

    [HttpGet("reports/{reportId}/evidence/text")]
    public async Task<IActionResult> GetReportTextEvidence(int reportId)
    {
        var report = await _db.DocumentReports
            .Include(r => r.Document)
            .FirstOrDefaultAsync(r => r.ReportId == reportId);

        if (report == null)
            return NotFound(new { message = "Không tìm thấy báo cáo vi phạm." });

        if (report.ReportedVersionId.HasValue)
        {
            var textRow = await _db.DocumentExtractedTexts
                .FirstOrDefaultAsync(t => t.DocumentId == report.DocumentId && t.DocumentVersionId == report.ReportedVersionId.Value);

            if (textRow == null || string.IsNullOrWhiteSpace(textRow.ExtractedText))
            {
                return NotFound(new
                {
                    message = "Văn bản trích xuất của phiên bản bị báo cáo hiện không có sẵn.",
                    documentId = report.DocumentId,
                    versionId = report.ReportedVersionId,
                    isVersionPinned = true,
                    isLegacyFallback = false
                });
            }

            return Ok(new
            {
                documentId = report.DocumentId,
                versionId = report.ReportedVersionId,
                extractedText = textRow.ExtractedText,
                isVersionPinned = true,
                isLegacyFallback = false
            });
        }
        else
        {
            // Legacy report without reported_version_id
            var legacyTextRow = await _db.DocumentExtractedTexts
                .Where(t => t.DocumentId == report.DocumentId)
                .OrderByDescending(t => t.ExtractionId)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                documentId = report.DocumentId,
                versionId = (int?)null,
                extractedText = legacyTextRow?.ExtractedText ?? string.Empty,
                isVersionPinned = false,
                isLegacyFallback = true
            });
        }
    }

    [HttpGet("appeals")]
    public async Task<IActionResult> Appeals() => Ok(await _db.ModerationAppeals.AsNoTracking().Include(a => a.Report).ThenInclude(r => r.Document)
        .OrderByDescending(a => a.CreatedAt).Select(a => new
        {
            a.AppealId,
            a.ReportId,
            a.Explanation,
            a.EvidenceUrl,
            a.Status,
            a.CreatedAt,
            a.Report.DocumentId,
            a.Report.Document.Title,
            a.Report.ReportType
        }).ToListAsync());

    [HttpGet("appeals/paged")]
    public async Task<IActionResult> AppealsPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.ModerationAppeals.AsNoTracking()
            .Include(a => a.Report).ThenInclude(r => r.Document)
            .Include(a => a.SubmittedByUser)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToUpperInvariant();
            query = query.Where(a => a.Status == s);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.AppealId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.AppealId,
                a.ReportId,
                a.Explanation,
                a.EvidenceUrl,
                a.Status,
                a.CreatedAt,
                a.ReviewedAt,
                a.ReviewNote,
                SubmittedByName = a.SubmittedByUser.Username,
                a.Report.DocumentId,
                DocumentTitle = a.Report.Document.Title,
                a.Report.ReportType
            })
            .ToListAsync();

        return Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        });
    }

    [HttpPost("appeals/{id}/{decision}")]
    public async Task<IActionResult> ResolveAppeal(int id, string decision, [FromBody] ModerationDecisionDto dto)
    {
        var appeal = await _db.ModerationAppeals.Include(a => a.Report).ThenInclude(r => r.Document).FirstOrDefaultAsync(a => a.AppealId == id);
        if (appeal == null || appeal.Status != "PENDING")
            return BadRequest();
        var normalized = decision.ToUpperInvariant();
        if (normalized == "RESTORE")
        {
            appeal.Status = "RESTORED";
            appeal.Report.Status = "RESTORED";
            appeal.Report.Document.SharingPermission = "PRIVATE";
            appeal.Report.Document.RequestedVisibility = "PRIVATE";
            appeal.Report.Document.ModerationStatus = "NOT_REQUESTED";
            appeal.Report.Document.ModerationSubmittedAt = null;
            appeal.Report.Document.IsFlagged = false;
        }
        else if (normalized == "UPHOLD")
        {
            appeal.Status = "UPHELD";
            appeal.Report.Status = "CLOSED";
            appeal.Report.Document.ModerationStatus = "HIDDEN";
            appeal.Report.Document.IsFlagged = true;
        }
        else
            return BadRequest();
        appeal.ReviewedByUserId = ActorId;
        appeal.ReviewedAt = _clock.UtcNow;
        appeal.ReviewNote = dto.Note?.Trim();
        _db.ModerationActions.Add(Action(appeal.Report.DocumentId, appeal.ReportId, normalized, "APPEALED", appeal.Status, dto.Note));
        _db.ModerationNotices.Add(new ModerationNotice
        {
            UserId = appeal.SubmittedByUserId,
            DocumentId = appeal.Report.DocumentId,
            ReportId = appeal.ReportId,
            Type = normalized == "RESTORE" ? "APPEAL_RESTORED" : "APPEAL_UPHELD",
            Title = normalized == "RESTORE" ? "Tài liệu đã được khôi phục" : "Quyết định xử lý được giữ nguyên",
            Message = normalized == "RESTORE"
                ? $"Giải trình cho tài liệu “{appeal.Report.Document.Title}” đã được chấp nhận. Tài liệu đã được khôi phục ở chế độ riêng tư; bạn có thể gửi yêu cầu xét duyệt công khai mới."
                : $"Giải trình cho tài liệu “{appeal.Report.Document.Title}” không làm thay đổi quyết định xử lý. {dto.Note}",
            ActionUrl = $"/notifications?reportId={appeal.ReportId}",
            IsRead = false,
            CreatedAt = _clock.UtcNow
        });
        await _db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("history")]
    public async Task<IActionResult> History() => Ok(await _db.ModerationActions.AsNoTracking().Include(a => a.Actor).OrderByDescending(a => a.CreatedAt).Take(200)
        .Select(a => new { a.ActionId, a.DocumentId, a.ReportId, a.Action, a.PreviousStatus, a.NewStatus, a.Note, a.CreatedAt, ActorName = a.Actor.Username }).ToListAsync());

    [HttpGet("history/paged")]
    public async Task<IActionResult> HistoryPaged(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.ModerationActions.AsNoTracking().Include(a => a.Actor);
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(a => a.CreatedAt)
            .ThenByDescending(a => a.ActionId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new
            {
                a.ActionId,
                a.DocumentId,
                a.ReportId,
                a.Action,
                a.PreviousStatus,
                a.NewStatus,
                a.Note,
                a.CreatedAt,
                ActorName = a.Actor.Username
            })
            .ToListAsync();

        return Ok(new PagedResult<object>
        {
            Items = items.Cast<object>().ToList(),
            TotalCount = totalCount,
            PageNumber = page,
            PageSize = pageSize
        });
    }

    private ModerationAction Action(int? documentId, int? reportId, string action, string? previous, string? next, string? note) =>
        new()
        {
            ActorUserId = ActorId,
            DocumentId = documentId,
            ReportId = reportId,
            Action = action,
            PreviousStatus = previous,
            NewStatus = next,
            Note = note?.Trim(),
            CreatedAt = _clock.UtcNow
        };

    private ModerationNotice Notice(int userId, int documentId, int? reportId, string type, string documentTitle, string message, bool canAppeal) =>
        new()
        {
            UserId = userId,
            DocumentId = documentId,
            ReportId = reportId,
            Type = type,
            Title = documentTitle,
            Message = message,
            CanAppeal = canAppeal,
            ActionUrl = canAppeal ? $"/notifications?reportId={reportId}" : $"/document/{documentId}",
            IsRead = false,
            CreatedAt = _clock.UtcNow
        };
}

public class ModerationDecisionDto
{
    public string? Note
    {
        get; set;
    }
}
