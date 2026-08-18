using System.Security.Claims;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Api.Controllers;

[Authorize(Roles = "ADMIN")]
[ApiController]
[Route("api/admin")]
public class AdminConfigurationController : ControllerBase
{
    private readonly IStudyHubDbContext _db;
    private readonly IDocumentService _documents;
    public AdminConfigurationController(IStudyHubDbContext db, IDocumentService documents)
    {
        _db = db;
        _documents = documents;
    }

    [HttpGet("documents")]
    public async Task<IActionResult> GetDocuments(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 8,
        [FromQuery] string? query = null,
        [FromQuery] string? status = null)
    {
        var documents = _db.Documents.Include(document => document.User).AsQueryable();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var keyword = query.Trim().ToLower();
            documents = documents.Where(document => document.Title.ToLower().Contains(keyword) || document.User.Username.ToLower().Contains(keyword));
        }
        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            documents = documents.Where(document => document.SharingPermission == status);
        }

        var totalCount = await documents.CountAsync();
        var items = await documents
            .OrderByDescending(document => document.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(document => new
            {
                document.DocumentId,
                document.Title,
                document.Subject,
                document.FileExtension,
                document.UserId,
                UploaderName = document.User.Username,
                document.SharingPermission,
                document.IsFlagged,
                document.TotalReportScore,
                document.BookmarkCount,
                document.DownloadCount,
                document.ViewCount,
                document.CreatedAt
            })
            .ToListAsync();

        return Ok(new {
            items,
            pageNumber,
            pageSize,
            totalCount
        });
    }

    [HttpGet("documents/{documentId}/detail")]
    public async Task<IActionResult> GetDocumentDetail(int documentId)
    {
        var detail = await _documents.GetDocumentDetailAsync(documentId);
        return detail == null ? NotFound(new
        {
            message = "Không tìm thấy tài liệu."
        }) : Ok(detail);
    }

    [HttpPut("documents/{documentId}/visibility")]
    public async Task<IActionResult> UpdateDocumentVisibility(int documentId, [FromBody] UpdateVisibilityDto dto)
    {
        var permission = dto.SharingPermission?.Trim().ToUpperInvariant();
        if (permission is not ("PUBLIC" or "PRIVATE"))
            return BadRequest(new
            {
                message = "Quyền chia sẻ không hợp lệ."
            });
        var document = await _db.Documents.FindAsync(documentId);
        if (document == null)
            return NotFound(new
            {
                message = "Không tìm thấy tài liệu."
            });
        document.SharingPermission = permission;
        if (permission == "PUBLIC")
            document.IsFlagged = false;
        await _db.SaveChangesAsync();
        return Ok(new
        {
            message = "Đã cập nhật quyền tài liệu."
        });
    }

    [HttpDelete("documents/{documentId}")]
    public async Task<IActionResult> DeleteDocument(int documentId)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !int.TryParse(claim.Value, out var adminId))
            return Unauthorized();
        return await _documents.DeleteDocumentAsync(adminId, documentId)
            ? Ok(new
            {
                message = "Đã xóa tài liệu."
            }) : NotFound(new
            {
                message = "Không tìm thấy tài liệu."
            });
    }

    [HttpGet("report-reasons")]
    public async Task<IActionResult> GetReportReasons() => Ok(await _db.ReportReasonConfigs.OrderBy(reason => reason.ReasonCode).ToListAsync());

    [HttpPost("report-reasons")]
    public async Task<IActionResult> CreateReportReason([FromBody] ReportReasonDto dto)
    {
        var code = dto.ReasonCode?.Trim().ToUpperInvariant();
        if (!IsValidReason(dto, code))
            return BadRequest(new
            {
                message = "Cấu hình lý do không hợp lệ."
            });
        if (await _db.ReportReasonConfigs.AnyAsync(reason => reason.ReasonCode == code))
            return Conflict(new
            {
                message = "Mã lý do đã tồn tại."
            });
        _db.ReportReasonConfigs.Add(new ReportReasonConfig { ReasonCode = code!, SeverityLevel = dto.SeverityLevel.Trim().ToUpperInvariant(), BaseScore = dto.BaseScore, AutoFlagThreshold = dto.AutoFlagThreshold, Description = dto.Description?.Trim() });
        await _db.SaveChangesAsync();
        return Ok(new
        {
            message = "Đã thêm lý do report."
        });
    }

    [HttpPut("report-reasons/{reasonCode}")]
    public async Task<IActionResult> UpdateReportReason(string reasonCode, [FromBody] ReportReasonDto dto)
    {
        var reason = await _db.ReportReasonConfigs.FindAsync(reasonCode);
        if (reason == null)
            return NotFound(new
            {
                message = "Không tìm thấy lý do report."
            });
        if (!IsValidReason(dto, reason.ReasonCode))
            return BadRequest(new
            {
                message = "Cấu hình lý do không hợp lệ."
            });
        reason.SeverityLevel = dto.SeverityLevel.Trim().ToUpperInvariant();
        reason.BaseScore = dto.BaseScore;
        reason.AutoFlagThreshold = dto.AutoFlagThreshold;
        reason.Description = dto.Description?.Trim();
        await _db.SaveChangesAsync();
        return Ok(new
        {
            message = "Đã cập nhật lý do report."
        });
    }

    [HttpDelete("report-reasons/{reasonCode}")]
    public async Task<IActionResult> DeleteReportReason(string reasonCode)
    {
        var reason = await _db.ReportReasonConfigs.FindAsync(reasonCode);
        if (reason == null)
            return NotFound(new
            {
                message = "Không tìm thấy lý do report."
            });
        if (await _db.DocumentReports.AnyAsync(report => report.ReasonCode == reasonCode))
            return Conflict(new
            {
                message = "Không thể xóa lý do đang được sử dụng."
            });
        _db.ReportReasonConfigs.Remove(reason);
        await _db.SaveChangesAsync();
        return Ok(new
        {
            message = "Đã xóa lý do report."
        });
    }

    [HttpGet("subscriptions")]
    public async Task<IActionResult> GetSubscriptions() => Ok(await _db.Subscriptions.OrderBy(subscription => subscription.TierId).ToListAsync());

    [HttpPut("subscriptions/{tierId}")]
    public async Task<IActionResult> UpdateSubscription(int tierId, [FromBody] SubscriptionConfigDto dto)
    {
        if (dto.MaxStorageMb < 0 || dto.TotalStorageMb < 0 || dto.AiPromptLimitPerDay < 0 || dto.Price < 0)
            return BadRequest(new
            {
                message = "Giá trị cấu hình không được âm."
            });
        var subscription = await _db.Subscriptions.FindAsync(tierId);
        if (subscription == null)
            return NotFound(new
            {
                message = "Không tìm thấy gói dịch vụ."
            });
        subscription.MaxStorageMb = dto.MaxStorageMb;
        subscription.TotalStorageMb = dto.TotalStorageMb;
        subscription.AiPromptLimitPerDay = dto.AiPromptLimitPerDay;
        subscription.Price = dto.Price;
        await _db.SaveChangesAsync();
        return Ok(new
        {
            message = "Đã cập nhật gói dịch vụ."
        });
    }

    [HttpGet("transfer-config")]
    public async Task<IActionResult> GetTransferConfiguration()
    {
        var config = await _db.TransferConfigurations.AsNoTracking().OrderBy(x => x.ConfigurationId).FirstOrDefaultAsync();
        return Ok(config ?? new TransferConfiguration());
    }

    [HttpPut("transfer-config")]
    public async Task<IActionResult> UpdateTransferConfiguration([FromBody] TransferConfigurationDto dto)
    {
        if (dto.IsActive && (string.IsNullOrWhiteSpace(dto.BankCode) || string.IsNullOrWhiteSpace(dto.AccountNumber) || string.IsNullOrWhiteSpace(dto.AccountName)))
            return BadRequest(new
            {
                message = "Vui lòng nhập đầy đủ ngân hàng, số tài khoản và chủ tài khoản."
            });
        var config = await _db.TransferConfigurations.OrderBy(x => x.ConfigurationId).FirstOrDefaultAsync();
        if (config == null)
        {
            config = new TransferConfiguration();
            _db.TransferConfigurations.Add(config);
        }
        config.BankCode = dto.BankCode.Trim().ToUpperInvariant();
        config.BankName = dto.BankName.Trim();
        config.AccountNumber = dto.AccountNumber.Trim();
        config.AccountName = dto.AccountName.Trim().ToUpperInvariant();
        config.QrTemplate = string.IsNullOrWhiteSpace(dto.QrTemplate) ? "compact2" : dto.QrTemplate.Trim();
        config.TransferContentPrefix = string.IsNullOrWhiteSpace(dto.TransferContentPrefix) ? "AIStudyHub" : dto.TransferContentPrefix.Trim();
        config.IsActive = dto.IsActive;
        config.UpdatedAt = DateTime.Now;
        await _db.SaveChangesAsync();
        return Ok(new
        {
            message = "Đã lưu cấu hình chuyển khoản."
        });
    }

    private static bool IsValidReason(ReportReasonDto dto, string? code) => !string.IsNullOrWhiteSpace(code) && code.Length <= 50 && !string.IsNullOrWhiteSpace(dto.SeverityLevel) && dto.BaseScore >= 0 && dto.AutoFlagThreshold > 0;
}

public class UpdateVisibilityDto
{
    public string SharingPermission { get; set; } = null!;
}
public class ReportReasonDto
{
    public string ReasonCode { get; set; } = null!; public string SeverityLevel { get; set; } = null!; public decimal BaseScore
    {
        get; set;
    }
    public decimal AutoFlagThreshold
    {
        get; set;
    }
    public string? Description
    {
        get; set;
    }
}
public class SubscriptionConfigDto
{
    public int MaxStorageMb
    {
        get; set;
    }
    public int TotalStorageMb
    {
        get; set;
    }
    public int AiPromptLimitPerDay
    {
        get; set;
    }
    public decimal Price
    {
        get; set;
    }
}
public class TransferConfigurationDto
{
    public string BankCode { get; set; } = ""; public string BankName { get; set; } = ""; public string AccountNumber { get; set; } = ""; public string AccountName { get; set; } = ""; public string QrTemplate { get; set; } = "compact2"; public string TransferContentPrefix { get; set; } = "AIStudyHub"; public bool IsActive
    {
        get; set;
    }
}
