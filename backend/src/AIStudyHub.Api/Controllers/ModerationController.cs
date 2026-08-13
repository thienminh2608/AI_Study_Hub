using System.Security.Claims;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Api.Controllers;

[Authorize(Roles = "ADMIN,MODERATOR")]
[ApiController]
[Route("api/moderation")]
public class ModerationController(IStudyHubDbContext db, IDocumentService documents) : ControllerBase
{
    private int ActorId => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet("queue")]
    public async Task<IActionResult> Queue() => Ok(await db.Documents.AsNoTracking().Include(d => d.User)
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

    [HttpGet("documents/{id}")]
    public async Task<IActionResult> DocumentDetail(int id)
    {
        var doc = await db.Documents.AsNoTracking().FirstOrDefaultAsync(d => d.DocumentId == id);
        if (doc == null || doc.ModerationStatus == "NOT_REQUESTED")
            return NotFound();
        return Ok(await documents.GetDocumentDetailAsync(id));
    }

    [HttpPost("documents/{id}/{decision}")]
    public async Task<IActionResult> ReviewDocument(int id, string decision, [FromBody] ModerationDecisionDto dto)
    {
        var doc = await db.Documents.FirstOrDefaultAsync(d => d.DocumentId == id);
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
        doc.ModeratedAt = DateTime.Now;
        doc.ModerationNote = dto.Note?.Trim();
        if (normalized is "REJECT" or "REQUEST-CHANGES")
            db.ModerationNotices.Add(Notice(doc.UserId, doc.DocumentId, null, normalized, doc.Title,
                normalized == "REJECT" ? $"Tài liệu đã bị từ chối công khai. Lý do: {dto.Note}" : $"Tài liệu cần được chỉnh sửa trước khi gửi duyệt lại. Nội dung: {dto.Note}", false));
        db.ModerationActions.Add(Action(id, null, normalized, previous, doc.ModerationStatus, dto.Note));
        await db.SaveChangesAsync();
        return Ok(new
        {
            doc.DocumentId,
            doc.ModerationStatus,
            doc.SharingPermission
        });
    }

    [HttpGet("reports")]
    public async Task<IActionResult> Reports() => Ok(await documents.GetReportsAsync());

    [HttpPost("reports/{id}/assign")]
    public async Task<IActionResult> Assign(int id)
    {
        var report = await db.DocumentReports.FindAsync(id);
        if (report == null || report.Status != "PENDING")
            return BadRequest();
        report.AssignedModeratorId = ActorId;
        report.Status = "IN_REVIEW";
        db.ModerationActions.Add(Action(report.DocumentId, id, "ASSIGN", "PENDING", "IN_REVIEW", null));
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpPost("reports/{id}/{decision}")]
    public async Task<IActionResult> Resolve(int id, string decision, [FromBody] ModerationDecisionDto dto)
    {
        var report = await db.DocumentReports.Include(r => r.Document).FirstOrDefaultAsync(r => r.ReportId == id);
        if (report == null || report.Status is not ("IN_REVIEW" or "RESTRICTED"))
            return BadRequest(new
            {
                message = "Báo cáo phải được nhận xử lý trước khi đưa ra quyết định."
            });
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
            report.RestrictedAt = DateTime.Now;
            report.Status = "RESTRICTED";
            report.Document.SharingPermission = "PRIVATE";
            report.Document.IsFlagged = true;
            db.ModerationNotices.Add(Notice(report.Document.UserId, report.DocumentId, report.ReportId, "TEMPORARILY_HIDDEN", report.Document.Title,
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
            db.ModerationNotices.Add(Notice(report.Document.UserId, report.DocumentId, report.ReportId, "VIOLATION_CONFIRMED", report.Document.Title,
                $"Moderator đã xác nhận tài liệu vi phạm và gỡ khỏi khu vực công khai. Bạn có quyền gửi giải trình. Căn cứ: {dto.Note}", true));
        }
        else
            return BadRequest(new
            {
                message = "Hành động xử lý báo cáo không hợp lệ."
            });

        if (report.Status != "RESTRICTED")
        {
            report.ResolvedByAdminId = ActorId;
            report.ResolvedAt = DateTime.Now;
        }
        report.ModeratorNote = dto.Note?.Trim();
        db.ModerationActions.Add(Action(report.DocumentId, id, normalized, previous, report.Status, dto.Note));
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("appeals")]
    public async Task<IActionResult> Appeals() => Ok(await db.ModerationAppeals.AsNoTracking().Include(a => a.Report).ThenInclude(r => r.Document)
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

    [HttpPost("appeals/{id}/{decision}")]
    public async Task<IActionResult> ResolveAppeal(int id, string decision, [FromBody] ModerationDecisionDto dto)
    {
        var appeal = await db.ModerationAppeals.Include(a => a.Report).ThenInclude(r => r.Document).FirstOrDefaultAsync(a => a.AppealId == id);
        if (appeal == null || appeal.Status != "PENDING")
            return BadRequest();
        var normalized = decision.ToUpperInvariant();
        if (normalized == "RESTORE")
        {
            appeal.Status = "RESTORED";
            appeal.Report.Status = "RESTORED";
            appeal.Report.Document.SharingPermission = "PUBLIC";
            appeal.Report.Document.ModerationStatus = "APPROVED";
            appeal.Report.Document.IsFlagged = false;
        }
        else if (normalized == "UPHOLD")
        {
            appeal.Status = "UPHELD";
            appeal.Report.Status = "CLOSED";
        }
        else
            return BadRequest();
        appeal.ReviewedByUserId = ActorId;
        appeal.ReviewedAt = DateTime.Now;
        appeal.ReviewNote = dto.Note?.Trim();
        db.ModerationActions.Add(Action(appeal.Report.DocumentId, appeal.ReportId, normalized, "APPEALED", appeal.Status, dto.Note));
        await db.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("history")]
    public async Task<IActionResult> History() => Ok(await db.ModerationActions.AsNoTracking().Include(a => a.Actor).OrderByDescending(a => a.CreatedAt).Take(200)
        .Select(a => new { a.ActionId, a.DocumentId, a.ReportId, a.Action, a.PreviousStatus, a.NewStatus, a.Note, a.CreatedAt, ActorName = a.Actor.Username }).ToListAsync());

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
            CreatedAt = DateTime.Now
        };

    private static ModerationNotice Notice(int userId, int documentId, int? reportId, string type, string documentTitle, string message, bool canAppeal) =>
        new()
        {
            UserId = userId,
            DocumentId = documentId,
            ReportId = reportId,
            Type = type,
            Title = documentTitle,
            Message = message,
            CanAppeal = canAppeal,
            IsRead = false,
            CreatedAt = DateTime.Now
        };
}

public class ModerationDecisionDto
{
    public string? Note
    {
        get; set;
    }
}
