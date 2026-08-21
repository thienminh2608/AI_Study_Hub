using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Api.Controllers;

[Authorize(Roles = "ADMIN")]
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IDocumentService _documentService;
    private readonly ITransactionService _transactionService;
    private readonly IAdminAnalyticsService _analyticsService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IStudyHubDbContext dbContext,
        IDocumentService documentService,
        ITransactionService transactionService,
        IAdminAnalyticsService analyticsService,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext;
        _documentService = documentService;
        _transactionService = transactionService;
        _analyticsService = analyticsService;
        _logger = logger;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardStats([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var stats = await _analyticsService.GetOverviewAnalyticsAsync(startDate, endDate);
        return Ok(stats);
    }

    [HttpGet("analytics/overview")]
    public async Task<IActionResult> GetOverviewAnalytics([FromQuery] DateTime? startDate, [FromQuery] DateTime? endDate)
    {
        var stats = await _analyticsService.GetOverviewAnalyticsAsync(startDate, endDate);
        return Ok(stats);
    }

    [HttpGet("analytics/top-contributors")]
    public async Task<IActionResult> GetTopContributors([FromQuery] int limit = 10)
    {
        var contributors = await _analyticsService.GetTopContributorsAsync(limit);
        return Ok(contributors);
    }

    [HttpGet("analytics/users/{userId}/contribution-score")]
    public async Task<IActionResult> GetUserContributionScore(int userId)
    {
        try
        {
            var score = await _analyticsService.CalculateUserContributionScoreAsync(userId);
            return Ok(score);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }
    }

    [HttpGet("analytics/documents/{documentId}/activities")]
    public async Task<IActionResult> GetDocumentActivitySummary(int documentId)
    {
        try
        {
            var summary = await _analyticsService.GetDocumentActivitySummaryAsync(documentId);
            return Ok(summary);
        }
        catch (KeyNotFoundException)
        {
            return NotFound(new { message = "Không tìm thấy tài liệu." });
        }
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetUsersPaginated(
        [FromQuery] int pageNumber = 1, 
        [FromQuery] int pageSize = 8, 
        [FromQuery] string? search = null, 
        [FromQuery] string? status = null)
    {
        var query = _dbContext.Users
            .Include(u => u.Tier)
            .AsQueryable();

        // Filters
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchClean = search.Trim().ToLower();
            query = query.Where(u => u.Username.ToLower().Contains(searchClean) || 
                                     (u.Email != null && u.Email.ToLower().Contains(searchClean)));
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            query = query.Where(u => u.Status == status);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.UserId,
                u.Username,
                u.Email,
                u.Role,
                u.TierId,
                TierName = u.Tier != null ? u.Tier.TierName : "Free",
                u.Balance,
                u.Status,
                u.IsAutoRenew,
                u.GracePeriodEndsAt,
                u.ExpiresAt,
                u.CreatedAt
            })
            .ToListAsync();

        return Ok(new PagedResult<object>(items.Cast<object>().ToList(), totalCount, pageNumber, pageSize));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] RegisterDto dto, [FromQuery] string tierType = "Free", [FromQuery] string role = "STUDENT")
    {
        var normalizedRole = role.Trim().ToUpperInvariant();
        if (normalizedRole is not ("STUDENT" or "MODERATOR" or "ADMIN"))
            return BadRequest(new { message = "Vai trò không hợp lệ." });
            
        var existingUser = await _dbContext.Users.AnyAsync(u => u.Email == dto.Email);
        if (existingUser)
            return BadRequest(new { message = "Email này đã được sử dụng." });

        int tierId = 2; // Default Free
        if ("Premium".Equals(tierType, StringComparison.OrdinalIgnoreCase))
        {
            tierId = 3;
        }
        else if ("Guest".Equals(tierType, StringComparison.OrdinalIgnoreCase))
        {
            tierId = 1;
        }

        var user = new User
        {
            Username = dto.Username,
            Email = dto.Email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Role = normalizedRole,
            TierId = tierId,
            Balance = 0,
            Status = "ACTIVE",
            IsAutoRenew = true,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Admin {AdminId} created user {UserId} with role {Role}", User.FindFirstValue(ClaimTypes.NameIdentifier), user.UserId, user.Role);
        return Ok(new { message = "Đã tạo tài khoản thành công." });
    }

    [HttpPut("users/{userId}")]
    public async Task<IActionResult> UpdateUser(int userId, [FromBody] UpdateUserDto dto)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
            return NotFound(new { message = "Không tìm thấy người dùng." });

        var normalizedRole = dto.Role.Trim().ToUpperInvariant();
        var normalizedStatus = dto.Status.Trim().ToUpperInvariant();
        if (normalizedRole is not ("STUDENT" or "MODERATOR" or "ADMIN") || normalizedStatus is not ("ACTIVE" or "SUSPENDED"))
            return BadRequest(new { message = "Vai trò hoặc trạng thái không hợp lệ." });
            
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) == userId.ToString() && normalizedStatus == "SUSPENDED")
            return BadRequest(new { message = "Bạn không thể tự khóa tài khoản đang đăng nhập." });

        user.Username = dto.Username;
        user.Email = dto.Email;
        user.Role = normalizedRole;
        user.Status = normalizedStatus;
        user.TierId = dto.TierId;
        user.UpdatedAt = DateTime.Now;

        // If user is suspended, privatize their documents
        if ("SUSPENDED".Equals(user.Status, StringComparison.OrdinalIgnoreCase))
        {
            var publicDocs = await _dbContext.Documents
                .Where(d => d.UserId == userId && d.SharingPermission == "PUBLIC")
                .ToListAsync();

            foreach (var doc in publicDocs)
            {
                doc.SharingPermission = "PRIVATE";
            }
        }

        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Admin {AdminId} updated user {UserId}; role={Role}; status={Status}; tier={TierId}", User.FindFirstValue(ClaimTypes.NameIdentifier), userId, user.Role, user.Status, user.TierId);
        return Ok(new { message = "Cập nhật thông tin người dùng thành công." });
    }

    [HttpDelete("users/{userId}")]
    public IActionResult DeleteUser(int userId)
    {
        return Conflict(new { message = "Không hỗ trợ xóa vĩnh viễn tài khoản. Hãy khóa tài khoản để bảo toàn dữ liệu và lịch sử đối soát." });
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactionsPaginated(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 8,
        [FromQuery] string? search = null,
        [FromQuery] string? status = null,
        [FromQuery] string? type = null,
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null)
    {
        var result = await _transactionService.GetTransactionsPaginatedAsync(pageNumber, pageSize, search, status, type, startDate, endDate);
        return Ok(result);
    }

    [HttpPut("transactions/{transactionId}")]
    public async Task<IActionResult> UpdateTransactionStatus(int transactionId, [FromBody] UpdateTransactionStatusDto dto)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !int.TryParse(claim.Value, out int adminId))
        {
            return Unauthorized();
        }

        bool success = await _transactionService.UpdateTransactionStatusAsync(transactionId, dto.Status, adminId, dto.FailureReason);
        if (success)
        {
            _logger.LogInformation("Admin {AdminId} changed transaction {TransactionId} to {Status}", adminId, transactionId, dto.Status);
            return Ok(new { message = "Đã cập nhật trạng thái giao dịch thành công." });
        }
        return Conflict(new { message = "Giao dịch đã được xử lý bởi request khác hoặc không còn ở trạng thái PENDING." });
    }

    [HttpPost("transactions/{transactionId}/refund")]
    public async Task<IActionResult> RefundTransaction(int transactionId, [FromBody] RefundRequestDto dto)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !int.TryParse(claim.Value, out int adminId))
        {
            return Unauthorized();
        }

        var originTx = await _dbContext.Transactions.FindAsync(transactionId);
        if (originTx == null)
        {
            return NotFound(new { message = "Không tìm thấy giao dịch." });
        }

        if (originTx.Type != "WITHDRAW" && originTx.Type != "PURCHASE")
        {
            return UnprocessableEntity(new { message = "Chỉ có thể hoàn tiền cho giao dịch mua gói hoặc rút tiền (WITHDRAW/PURCHASE). Giao dịch nạp tiền phải dùng chức năng Thu hồi nạp tiền." });
        }

        if (originTx.Status != "SUCCESS")
        {
            return UnprocessableEntity(new { message = "Chỉ có thể hoàn tiền cho giao dịch đã hoàn thành thành công." });
        }

        bool alreadyRefunded = await _dbContext.Transactions
            .AnyAsync(t => (t.OriginalTransactionId == transactionId || (t.Type == "REFUND" && t.ReferenceCode == transactionId.ToString())) && t.Status == "SUCCESS");
        if (alreadyRefunded)
        {
            return Conflict(new { message = "Giao dịch này đã được hoàn tiền trước đó." });
        }

        bool success = await _transactionService.RefundTransactionAsync(transactionId, adminId, dto.Reason);
        if (success)
        {
            _logger.LogInformation("Admin {AdminId} refunded transaction {TransactionId}. Reason: {Reason}", adminId, transactionId, dto.Reason);
            return Ok(new { message = "Đã thực hiện hoàn tiền giao dịch thành công." });
        }
        return StatusCode(500, new { message = "Không thể hoàn tiền giao dịch do lỗi hệ thống." });
    }

    [HttpPost("transactions/{transactionId}/reverse-deposit")]
    public async Task<IActionResult> ReverseDeposit(int transactionId, [FromBody] ReverseDepositDto dto)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !int.TryParse(claim.Value, out int adminId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return BadRequest(new { message = "Lý do thu hồi giao dịch là bắt buộc." });
        }

        var originTx = await _dbContext.Transactions.FindAsync(transactionId);
        if (originTx == null)
        {
            return NotFound(new { message = "Không tìm thấy giao dịch nạp tiền." });
        }

        if (originTx.Type != "DEPOSIT" || originTx.Status != "SUCCESS")
        {
            return UnprocessableEntity(new { message = "Chỉ có thể thu hồi giao dịch nạp tiền (DEPOSIT) đã thành công." });
        }

        bool alreadyReversed = await _dbContext.Transactions
            .AnyAsync(t => t.OriginalTransactionId == transactionId && t.Status == "SUCCESS");
        if (alreadyReversed)
        {
            return Conflict(new { message = "Giao dịch nạp tiền này đã được thu hồi trước đó." });
        }

        var user = await _dbContext.Users.FindAsync(originTx.UserId);
        if (user == null || (user.Balance ?? 0) < originTx.Amount)
        {
            return UnprocessableEntity(new { message = "Số dư ví của người dùng không đủ để thu hồi giao dịch nạp tiền." });
        }

        bool success = await _transactionService.ReverseDepositAsync(transactionId, adminId, dto.Reason);
        if (success)
        {
            _logger.LogInformation("Admin {AdminId} reversed deposit transaction {TransactionId}. Reason: {Reason}", adminId, transactionId, dto.Reason);
            return Ok(new { message = "Đã thu hồi giao dịch nạp tiền thành công." });
        }
        return StatusCode(500, new { message = "Không thể thực hiện thu hồi giao dịch do lỗi hệ thống." });
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports(
        [FromQuery] int pageNumber = 1, 
        [FromQuery] int pageSize = 8, 
        [FromQuery] string? search = null, 
        [FromQuery] string? status = null)
    {
        var query = _dbContext.DocumentReports
            .Include(r => r.Document)
            .Include(r => r.Reporter)
            .Include(r => r.ResolvedByAdmin)
            .AsQueryable();

        // Filters
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchClean = search.Trim().ToLower();
            query = query.Where(r => r.Document.Title.ToLower().Contains(searchClean) || 
                                     r.Reporter.Username.ToLower().Contains(searchClean));
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            if (status == "RESOLVED")
            {
                query = query.Where(r => r.Status != "PENDING");
            }
            else
            {
                query = query.Where(r => r.Status == status);
            }
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(r => new DocumentReportResponseDto
        {
            ReportId = r.ReportId,
            DocumentId = r.DocumentId,
            DocumentTitle = r.Document.Title,
            ReporterId = r.ReporterId,
            ReporterName = r.Reporter.Username,
            ReasonCode = r.ReasonCode,
            AdditionalDetails = r.AdditionalDetails,
            Status = r.Status ?? "PENDING",
            CreatedAt = r.CreatedAt,
            ResolvedAt = r.ResolvedAt,
            ResolvedByAdminName = r.ResolvedByAdmin?.Username,
            ReportType = r.ReportType,
            ClaimantName = r.ClaimantName,
            ClaimantEmail = r.ClaimantEmail,
            OriginalWorkUrl = r.OriginalWorkUrl,
            EvidenceDescription = r.EvidenceDescription,
            ModeratorNote = r.ModeratorNote,
            AssignedModeratorId = r.AssignedModeratorId,
            PreviousSharingPermission = r.PreviousSharingPermission,
            RestrictedAt = r.RestrictedAt,
            ReportedVersionId = r.ReportedVersionId
        }).ToList();

        return Ok(new PagedResult<DocumentReportResponseDto>(dtos, totalCount, pageNumber, pageSize));
    }

    [HttpPost("reports/{reportId}/resolve")]
    public async Task<IActionResult> ResolveReport(int reportId, [FromQuery] string action)
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (claim == null || !int.TryParse(claim.Value, out int adminId))
        {
            return Unauthorized();
        }

        bool success = await _documentService.ResolveReportAsync(adminId, reportId, action);
        if (success)
        {
            _logger.LogInformation("Admin {AdminId} resolved report {ReportId} with action {Action}", adminId, reportId, action);
            return Ok(new { message = "Xử lý báo cáo thành công." });
        }
        return BadRequest(new { message = "Không thể xử lý báo cáo." });
    }

    [HttpGet("ai-observability/summary")]
    public async Task<IActionResult> GetAiObservabilitySummary()
    {
        var totalRequests = await _dbContext.AiUsages.CountAsync();
        var totalPromptTokens = await _dbContext.AiUsages.SumAsync(u => (long)u.PromptTokens);
        var totalCompletionTokens = await _dbContext.AiUsages.SumAsync(u => (long)u.CompletionTokens);
        var totalCachedTokens = await _dbContext.AiUsages.SumAsync(u => (long)u.CachedTokens);
        var totalCost = await _dbContext.AiUsages.SumAsync(u => (decimal?)u.EstimatedCost) ?? 0m;
        var avgLatency = totalRequests > 0 ? await _dbContext.AiUsages.AverageAsync(u => (double)u.LatencyMs) : 0;
        var errorCount = await _dbContext.AiUsages.CountAsync(u => u.Status == "ERROR");

        var byModel = await _dbContext.AiUsages
            .GroupBy(u => u.Model)
            .Select(g => new { Model = g.Key, Count = g.Count(), Tokens = g.Sum(x => (long)x.TotalTokens), Cost = g.Sum(x => x.EstimatedCost) })
            .ToListAsync();

        var byOperation = await _dbContext.AiUsages
            .GroupBy(u => u.Operation)
            .Select(g => new { Operation = g.Key, Count = g.Count(), Tokens = g.Sum(x => (long)x.TotalTokens), Cost = g.Sum(x => x.EstimatedCost) })
            .ToListAsync();

        return Ok(new
        {
            TotalRequests = totalRequests,
            TotalPromptTokens = totalPromptTokens,
            TotalCompletionTokens = totalCompletionTokens,
            TotalCachedTokens = totalCachedTokens,
            TotalTokens = totalPromptTokens + totalCompletionTokens,
            TotalCostUsd = Math.Round(totalCost, 4),
            AvgLatencyMs = Math.Round(avgLatency, 1),
            ErrorCount = errorCount,
            ErrorRatePercent = totalRequests > 0 ? Math.Round((double)errorCount / totalRequests * 100, 2) : 0,
            ByModel = byModel,
            ByOperation = byOperation
        });
    }

    [HttpGet("ai-observability/usages")]
    public async Task<IActionResult> GetAiObservabilityUsages([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 20)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var query = _dbContext.AiUsages.AsNoTracking().Include(u => u.User);
        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .ThenByDescending(u => u.UsageId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new
            {
                u.UsageId,
                u.UserId,
                Username = u.User.Username,
                u.Provider,
                u.Model,
                u.Operation,
                u.PromptTokens,
                u.CompletionTokens,
                u.CachedTokens,
                u.TotalTokens,
                u.LatencyMs,
                u.Status,
                u.ErrorCode,
                u.EstimatedCost,
                u.Currency,
                u.RequestId,
                u.CreatedAt
            })
            .ToListAsync();

        return Ok(new PagedResult<object>(items.Cast<object>().ToList(), totalCount, pageNumber, pageSize));
    }
}

public class UpdateUserDto
{
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int Balance { get; set; }
    public int TierId { get; set; }
}
