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
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IStudyHubDbContext dbContext,
        IDocumentService documentService,
        ITransactionService transactionService,
        ILogger<AdminController> logger)
    {
        _dbContext = dbContext;
        _documentService = documentService;
        _transactionService = transactionService;
        _logger = logger;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardStats()
    {
        int totalUsers = await _dbContext.Users.CountAsync();
        int activeUsers = await _dbContext.Users.CountAsync(u => u.Status == "ACTIVE");
        int suspendedUsers = await _dbContext.Users.CountAsync(u => u.Status == "SUSPENDED");
        int premiumUsers = await _dbContext.Users.CountAsync(u => u.TierId == 3);
        int studentUsers = await _dbContext.Users.CountAsync(u => u.Role == "STUDENT");
        int freeUsers = await _dbContext.Users.CountAsync(u => u.Role == "STUDENT" && u.TierId == 2);
        int totalTransactions = await _dbContext.Transactions.CountAsync();
        int pendingTransactions = await _dbContext.Transactions.CountAsync(t => t.Status == "PENDING");
        decimal successfulDeposits = await _dbContext.Transactions.Where(t => t.Status == "SUCCESS" && t.Type == "DEPOSIT").SumAsync(t => (decimal?)t.Amount) ?? 0;
        int totalDocuments = await _dbContext.Documents.CountAsync();
        int publicDocuments = await _dbContext.Documents.CountAsync(d => d.SharingPermission == "PUBLIC");
        int privateDocuments = await _dbContext.Documents.CountAsync(d => d.SharingPermission == "PRIVATE");
        int flaggedDocuments = await _dbContext.Documents.CountAsync(d => d.IsFlagged == true);
        int totalReports = await _dbContext.DocumentReports.CountAsync();
        int pendingReports = await _dbContext.DocumentReports.CountAsync(r => r.Status == "PENDING");
        var recentTransactions = await _dbContext.Transactions.Include(t => t.User).OrderByDescending(t => t.StartedAt).Take(5).Select(t => new { t.TransactionId, t.User.Username, t.Amount, t.Status, t.StartedAt }).ToListAsync();
        var recentReports = await _dbContext.DocumentReports.Include(r => r.Reporter).Include(r => r.Document).OrderByDescending(r => r.CreatedAt).Take(5).Select(r => new { r.ReportId, r.Document.Title, ReporterName = r.Reporter.Username, r.ReasonCode, r.Status, r.CreatedAt }).ToListAsync();

        return Ok(new
        {
            totalUsers,
            activeUsers,
            suspendedUsers,
            premiumUsers,
            studentUsers,
            freeUsers,
            totalTransactions,
            pendingTransactions,
            successfulDeposits,
            totalDocuments,
            publicDocuments,
            privateDocuments,
            flaggedDocuments,
            totalReports,
            pendingReports,
            recentTransactions,
            recentReports
        });
    }

    [HttpGet("users")]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _dbContext.Users
            .Include(u => u.Tier)
            .OrderByDescending(u => u.CreatedAt)
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
                u.ExpiresAt,
                u.CreatedAt
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] RegisterDto dto, [FromQuery] string tierType = "Free", [FromQuery] string role = "STUDENT")
    {
        var normalizedRole = role.Trim().ToUpperInvariant();
        if (normalizedRole is not ("STUDENT" or "MODERATOR" or "ADMIN"))
            return BadRequest(new
            {
                message = "Vai trò không hợp lệ."
            });
        var existingUser = await _dbContext.Users.AnyAsync(u => u.Email == dto.Email);
        if (existingUser)
        {
            return BadRequest(new
            {
                message = "Email này đã được sử dụng."
            });
        }

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
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        _logger.LogInformation("Admin {AdminId} created user {UserId} with role {Role}", User.FindFirstValue(ClaimTypes.NameIdentifier), user.UserId, user.Role);
        return Ok(new
        {
            message = "Đã tạo tài khoản thành công."
        });
    }

    [HttpPut("users/{userId}")]
    public async Task<IActionResult> UpdateUser(int userId, [FromBody] UpdateUserDto dto)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound(new
            {
                message = "Không tìm thấy người dùng."
            });
        }

        var normalizedRole = dto.Role.Trim().ToUpperInvariant();
        var normalizedStatus = dto.Status.Trim().ToUpperInvariant();
        if (normalizedRole is not ("STUDENT" or "MODERATOR" or "ADMIN") || normalizedStatus is not ("ACTIVE" or "SUSPENDED"))
            return BadRequest(new
            {
                message = "Vai trò hoặc trạng thái không hợp lệ."
            });
        if (User.FindFirstValue(ClaimTypes.NameIdentifier) == userId.ToString() && normalizedStatus == "SUSPENDED")
            return BadRequest(new
            {
                message = "Bạn không thể tự khóa tài khoản đang đăng nhập."
            });

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
        return Ok(new
        {
            message = "Cập nhật thông tin người dùng thành công."
        });
    }

    [HttpDelete("users/{userId}")]
    public IActionResult DeleteUser(int userId)
    {
        return Conflict(new
        {
            message = "Không hỗ trợ xóa vĩnh viễn tài khoản. Hãy khóa tài khoản để bảo toàn dữ liệu và lịch sử đối soát."
        });
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetAllTransactions()
    {
        var txs = await _transactionService.GetAllTransactionsAsync();
        return Ok(txs);
    }

    [HttpPut("transactions/{transactionId}")]
    public async Task<IActionResult> UpdateTransactionStatus(int transactionId, [FromBody] UpdateTransactionStatusDto dto)
    {
        bool success = await _transactionService.UpdateTransactionStatusAsync(transactionId, dto.Status);
        if (success)
        {
            _logger.LogInformation("Admin {AdminId} changed transaction {TransactionId} to {Status}", User.FindFirstValue(ClaimTypes.NameIdentifier), transactionId, dto.Status);
            return Ok(new
            {
                message = "Đã cập nhật trạng thái giao dịch thành công."
            });
        }
        return BadRequest(new
        {
            message = "Không thể cập nhật giao dịch (có thể giao dịch đã được xử lý trước đó)."
        });
    }

    [HttpGet("reports")]
    public async Task<IActionResult> GetReports()
    {
        var reports = await _documentService.GetReportsAsync();
        return Ok(reports);
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
            return Ok(new
            {
                message = "Xử lý báo cáo thành công."
            });
        }
        return BadRequest(new
        {
            message = "Không thể xử lý báo cáo."
        });
    }
}

public class UpdateUserDto
{
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string Role { get; set; } = null!;
    public string Status { get; set; } = null!;
    public int Balance
    {
        get; set;
    }
    public int TierId
    {
        get; set;
    }
}
