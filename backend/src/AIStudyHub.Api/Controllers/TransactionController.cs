using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Api.Controllers;

[Authorize(Roles = "STUDENT")]
[ApiController]
[Route("api/transaction")]
public class TransactionController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly IStudyHubDbContext _db;

    public TransactionController(ITransactionService transactionService, IStudyHubDbContext db)
    {
        _transactionService = transactionService;
        _db = db;
    }

    [HttpGet("transfer-config")]
    public async Task<IActionResult> GetTransferConfiguration()
    {
        var config = await _db.TransferConfigurations.AsNoTracking().OrderBy(x => x.ConfigurationId).FirstOrDefaultAsync();
        if (config == null || !config.IsActive || string.IsNullOrWhiteSpace(config.BankCode) || string.IsNullOrWhiteSpace(config.AccountNumber))
            return Ok(new
            {
                isActive = false
            });
        return Ok(new
        {
            config.BankCode,
            config.BankName,
            config.AccountNumber,
            config.AccountName,
            config.QrTemplate,
            config.TransferContentPrefix,
            config.IsActive
        });
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

    [HttpPost]
    public async Task<IActionResult> CreateTransaction([FromBody] CreateTransactionDto dto)
    {
        try
        {
            int userId = GetCurrentUserId();
            bool success = await _transactionService.CreateTransactionAsync(userId, dto);
            if (success)
            {
                return Ok(new
                {
                    message = "Đã gửi yêu cầu giao dịch thành công. Vui lòng đợi Admin duyệt."
                });
            }
            return BadRequest(new
            {
                message = "Không thể tạo giao dịch."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetUserTransactions()
    {
        try
        {
            int userId = GetCurrentUserId();
            var txs = await _transactionService.GetUserTransactionsAsync(userId);
            return Ok(txs);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }

    [HttpPost("buy-premium")]
    public async Task<IActionResult> BuyPremium()
    {
        try
        {
            int userId = GetCurrentUserId();
            bool success = await _transactionService.BuyPremiumAsync(userId);
            if (success)
            {
                return Ok(new
                {
                    message = "Kích hoạt gói Premium thành công! Bạn có thêm 30 ngày sử dụng và các giới hạn dung lượng/AI tăng lên."
                });
            }
            return BadRequest(new
            {
                message = "Giao dịch Premium thất bại."
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                message = ex.Message
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = $"Lỗi hệ thống: {ex.Message}"
            });
        }
    }

    [AllowAnonymous]
    [HttpGet("tiers")]
    public async Task<IActionResult> GetSubscriptionTiers()
    {
        try
        {
            var tiers = await _transactionService.GetSubscriptionTiersAsync();
            return Ok(tiers);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = ex.Message
            });
        }
    }
}
