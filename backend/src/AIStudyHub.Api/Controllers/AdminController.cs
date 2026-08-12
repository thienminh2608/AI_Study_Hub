using System;
using System.Linq;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AIStudyHub.Api.Controllers;

[Authorize(Roles = "ADMIN")]
[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IDocumentService _documentService;
    private readonly ITransactionService _transactionService;

    public AdminController(
        IStudyHubDbContext dbContext,
        IDocumentService documentService,
        ITransactionService transactionService)
    {
        _dbContext = dbContext;
        _documentService = documentService;
        _transactionService = transactionService;
    }

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboardStats()
    {
        int totalUsers = await _dbContext.Users.CountAsync();
        int totalTransactions = await _dbContext.Transactions.CountAsync();
        int totalDocuments = await _dbContext.Documents.CountAsync();
        int totalReports = await _dbContext.DocumentReports.CountAsync();

        return Ok(new
        {
            totalUsers,
            totalTransactions,
            totalDocuments,
            totalReports
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
        var existingUser = await _dbContext.Users.AnyAsync(u => u.Email == dto.Email);
        if (existingUser)
        {
            return BadRequest(new { message = "Email này đã được sử dụng." });
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
            Role = role.ToUpper(),
            TierId = tierId,
            Balance = 0,
            Status = "ACTIVE",
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();
        return Ok(new { message = "Đã tạo tài khoản thành công." });
    }

    [HttpPut("users/{userId}")]
    public async Task<IActionResult> UpdateUser(int userId, [FromBody] UpdateUserDto dto)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return NotFound(new { message = "Không tìm thấy người dùng." });
        }

        user.Username = dto.Username;
        user.Email = dto.Email;
        user.Role = dto.Role.ToUpper();
        user.Status = dto.Status.ToUpper();
        user.Balance = dto.Balance;
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
        return Ok(new { message = "Cập nhật thông tin người dùng thành công." });
    }

    [HttpDelete("users/{userId}")]
    public async Task<IActionResult> DeleteUser(int userId)
    {
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return NotFound(new { message = "Không tìm thấy người dùng." });

        _dbContext.Users.Remove(user);
        await _dbContext.SaveChangesAsync();
        return Ok(new { message = "Đã xóa người dùng và dữ liệu liên quan thành công." });
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
            return Ok(new { message = "Đã cập nhật trạng thái giao dịch thành công." });
        }
        return BadRequest(new { message = "Không thể cập nhật giao dịch (có thể giao dịch đã được xử lý trước đó)." });
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
            return Ok(new { message = "Xử lý báo cáo thành công." });
        }
        return BadRequest(new { message = "Không thể xử lý báo cáo." });
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
