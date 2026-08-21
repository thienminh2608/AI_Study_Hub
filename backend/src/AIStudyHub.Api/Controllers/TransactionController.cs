using System;
using System.Security.Claims;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/transaction")]
public class TransactionController : ControllerBase
{
    private readonly ITransactionService _transactionService;
    private readonly IStudyHubDbContext _db;
    private readonly IPayOsService _payOsService;
    private readonly IClock _clock;
    private readonly IConfiguration _configuration;

    public TransactionController(
        ITransactionService transactionService,
        IStudyHubDbContext db,
        IPayOsService payOsService,
        IClock clock,
        IConfiguration configuration)
    {
        _transactionService = transactionService;
        _db = db;
        _payOsService = payOsService;
        _clock = clock;
        _configuration = configuration;
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

    [HttpGet("{transactionId}/invoice")]
    [Authorize]
    public async Task<IActionResult> GetInvoice(int transactionId)
    {
        try
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirst(ClaimTypes.Role);
            if (claim == null || !int.TryParse(claim.Value, out int userId))
                return Unauthorized();

            var tx = await _db.Transactions
                .Include(t => t.User)
                .FirstOrDefaultAsync(t => t.TransactionId == transactionId);

            if (tx == null)
                return NotFound(new { message = "Không tìm thấy giao dịch." });

            // Only owner or ADMIN can view invoice
            if (tx.UserId != userId && roleClaim?.Value != "ADMIN")
                return Forbid();

            if (tx.Status != "SUCCESS")
                return BadRequest(new { message = "Chưa thể xuất hóa đơn cho giao dịch chưa thành công." });

            string description = tx.Type == "DEPOSIT" ? "Nạp tiền vào ví điện tử AI Study Hub" : 
                                 tx.Type == "WITHDRAW" ? "Thanh toán đăng ký gói Premium học tập" : 
                                 "Hoàn trả tiền giao dịch";

            return Ok(new
            {
                transactionId = tx.TransactionId,
                username = tx.User.Username,
                email = tx.User.Email,
                amount = tx.Amount,
                type = tx.Type,
                status = tx.Status,
                date = tx.CompletedAt ?? tx.StartedAt,
                description = description,
                invoiceNumber = $"INV-{tx.CompletedAt?.ToString("yyyyMMdd")}-{tx.TransactionId}",
                companyName = "Hệ thống Học tập Thông minh AI Study Hub",
                taxCode = "0109876543-Reconciliation",
                address = "Km 9, Đường Nguyễn Trãi, Thanh Xuân, Hà Nội"
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = ex.Message });
        }
    }

    [HttpPost("payos/create-link")]
    public async Task<IActionResult> CreatePayOsPaymentLink([FromBody] CreatePayOsPaymentLinkDto dto)
    {
        var userId = GetCurrentUserId();
        if (dto.Amount < 2000 || dto.Amount % 1 != 0)
        {
            return BadRequest(new { message = "Số tiền nạp tối thiểu là 2,000 VND và phải là số nguyên." });
        }

        long amountLong = (long)dto.Amount;
        var frontendBaseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";

        // Generate cryptographically secure random 64-bit PayOS orderCode (within safe range [100000, 9007199254740991])
        Domain.Entities.Transaction? transaction = null;
        long orderCode = 0;

        for (int attempt = 0; attempt < 3; attempt++)
        {
            // Range: 100_000 to 9_000_000_000_000_000
            byte[] bytes = new byte[8];
            System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
            long rawLong = BitConverter.ToInt64(bytes, 0) & 0x7FFFFFFFFFFFFFFFL;
            orderCode = 100000L + (rawLong % 9000000000000000L);

            var candidate = new Domain.Entities.Transaction
            {
                UserId = userId,
                Amount = amountLong,
                Type = "DEPOSIT",
                Status = "CREATING",
                PayOsOrderCode = orderCode,
                StartedAt = _clock.Now,
                RequiresManualReview = false
            };

            try
            {
                _db.Transactions.Add(candidate);
                await _db.SaveChangesAsync();
                transaction = candidate;
                break;
            }
            catch (DbUpdateException)
            {
                _db.ChangeTracker.Clear();
            }
        }

        if (transaction == null)
        {
            return StatusCode(500, new { message = "Không thể khởi tạo mã đơn hàng duy nhất. Vui lòng thử lại." });
        }

        var user = await _db.Users.FindAsync(userId);

        try
        {
            var payOsRequest = new CreatePaymentLinkRequestDto
            {
                OrderCode = orderCode,
                Amount = amountLong,
                Description = $"Nap vi {orderCode}",
                BuyerName = user?.Username ?? "Student",
                BuyerEmail = user?.Email ?? "",
                ReturnUrl = $"{frontendBaseUrl}/payment/success?orderCode={orderCode}",
                CancelUrl = $"{frontendBaseUrl}/payment/cancel?orderCode={orderCode}"
            };

            var payOsRes = await _payOsService.CreatePaymentLinkAsync(payOsRequest, HttpContext.RequestAborted);

            transaction.PaymentLinkId = payOsRes.PaymentLinkId;
            transaction.Status = "PENDING";
            await _db.SaveChangesAsync();

            return Ok(new
            {
                transactionId = transaction.TransactionId,
                orderCode = orderCode,
                checkoutUrl = payOsRes.CheckoutUrl,
                qrCode = payOsRes.QrCode,
                amount = amountLong,
                status = transaction.Status
            });
        }
        catch (Exception ex)
        {
            transaction.Status = "CREATE_FAILED";
            transaction.FailureReason = ex.Message;
            await _db.SaveChangesAsync();
            return StatusCode(500, new { message = $"Không thể tạo link thanh toán PayOS: {ex.Message}" });
        }
    }

    [HttpPost("payos/retry/{transactionId}")]
    public async Task<IActionResult> RetryPayOsPaymentLink(int transactionId)
    {
        var userId = GetCurrentUserId();

        // Conditional claim CREATE_FAILED -> CREATING
        var claimed = await _db.Transactions
            .Where(t => t.TransactionId == transactionId && t.UserId == userId && t.Status == "CREATE_FAILED")
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.Status, "CREATING"), HttpContext.RequestAborted);

        if (claimed != 1)
        {
            return BadRequest(new { message = "Giao dịch không tồn tại hoặc không ở trạng thái có thể thử lại." });
        }

        var transaction = await _db.Transactions.FindAsync(transactionId);
        if (transaction == null || transaction.PayOsOrderCode == null)
        {
            return NotFound(new { message = "Không tìm thấy thông tin giao dịch." });
        }

        long orderCode = transaction.PayOsOrderCode.Value;
        var frontendBaseUrl = _configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";
        var user = await _db.Users.FindAsync(userId);

        try
        {
            // 1. Query PayOS first in case payment link was already created during previous timeout
            var existingInfo = await _payOsService.GetPaymentRequestAsync(orderCode, HttpContext.RequestAborted);
            if (existingInfo != null && !string.IsNullOrWhiteSpace(existingInfo.Status))
            {
                string? validCheckoutUrl = !string.IsNullOrWhiteSpace(existingInfo.CheckoutUrl)
                    ? existingInfo.CheckoutUrl
                    : (!string.IsNullOrWhiteSpace(existingInfo.Id)
                        ? $"https://pay.payos.vn/web/{existingInfo.Id}"
                        : (!string.IsNullOrWhiteSpace(transaction.PaymentLinkId)
                            ? $"https://pay.payos.vn/web/{transaction.PaymentLinkId}"
                            : null));

                if (!string.IsNullOrWhiteSpace(validCheckoutUrl))
                {
                    transaction.Status = "PENDING";
                    if (!string.IsNullOrWhiteSpace(existingInfo.Id))
                    {
                        transaction.PaymentLinkId = existingInfo.Id;
                    }
                    await _db.SaveChangesAsync();

                    return Ok(new
                    {
                        transactionId = transaction.TransactionId,
                        orderCode = orderCode,
                        checkoutUrl = validCheckoutUrl,
                        amount = transaction.Amount,
                        status = transaction.Status
                    });
                }
            }

            // 2. Call create link with reused orderCode
            var payOsRequest = new CreatePaymentLinkRequestDto
            {
                OrderCode = orderCode,
                Amount = transaction.Amount,
                Description = $"Nap vi {orderCode}",
                BuyerName = user?.Username ?? "Student",
                BuyerEmail = user?.Email ?? "",
                ReturnUrl = $"{frontendBaseUrl}/payment/success?orderCode={orderCode}",
                CancelUrl = $"{frontendBaseUrl}/payment/cancel?orderCode={orderCode}"
            };

            var payOsRes = await _payOsService.CreatePaymentLinkAsync(payOsRequest, HttpContext.RequestAborted);

            transaction.PaymentLinkId = payOsRes.PaymentLinkId;
            transaction.Status = "PENDING";
            await _db.SaveChangesAsync();

            return Ok(new
            {
                transactionId = transaction.TransactionId,
                orderCode = orderCode,
                checkoutUrl = payOsRes.CheckoutUrl,
                qrCode = payOsRes.QrCode,
                amount = transaction.Amount,
                status = transaction.Status
            });
        }
        catch (Exception ex)
        {
            transaction.Status = "CREATE_FAILED";
            transaction.FailureReason = ex.Message;
            await _db.SaveChangesAsync();
            return StatusCode(500, new { message = $"Thử lại tạo link PayOS thất bại: {ex.Message}" });
        }
    }

    [HttpGet("payos/{orderCode}/status")]
    public async Task<IActionResult> GetPayOsOrderStatus(long orderCode)
    {
        var userId = GetCurrentUserId();
        var tx = await _db.Transactions.FirstOrDefaultAsync(t => t.PayOsOrderCode == orderCode);
        if (tx == null)
        {
            return NotFound(new { message = "Không tìm thấy giao dịch." });
        }

        var roleClaim = User.FindFirst(ClaimTypes.Role)?.Value;
        if (tx.UserId != userId && roleClaim != "ADMIN")
        {
            return Forbid();
        }

        return Ok(new
        {
            transactionId = tx.TransactionId,
            orderCode = tx.PayOsOrderCode,
            amount = tx.Amount,
            status = tx.Status,
            completedAt = tx.CompletedAt,
            requiresManualReview = tx.RequiresManualReview
        });
    }
}

public class CreatePayOsPaymentLinkDto
{
    public decimal Amount { get; set; }
}


