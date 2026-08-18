using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AIStudyHub.Application.Services;

public class TransactionService : ITransactionService
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IClock _clock;
    private const string LedgerSecretKey = "AIStudyHubSecureLedgerKey";

    public TransactionService(IStudyHubDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<List<TransactionDto>> GetUserTransactionsAsync(int userId)
    {
        var txs = await _dbContext.Transactions
            .Include(t => t.User)
            .Include(t => t.Approver)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.StartedAt)
            .ToListAsync();

        return txs.Select(t => MapToDto(t, t.User.Username)).ToList();
    }

    public async Task<List<TransactionDto>> GetAllTransactionsAsync()
    {
        var txs = await _dbContext.Transactions
            .Include(t => t.User)
            .Include(t => t.Approver)
            .OrderByDescending(t => t.StartedAt)
            .ToListAsync();

        return txs.Select(t => MapToDto(t, t.User.Username)).ToList();
    }

    public async Task<PagedResult<TransactionDto>> GetTransactionsPaginatedAsync(int pageNumber, int pageSize, string? search, string? status, string? type)
    {
        var query = _dbContext.Transactions
            .Include(t => t.User)
            .Include(t => t.Approver)
            .AsQueryable();

        // Apply Filters
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchClean = search.Trim().ToLower();
            query = query.Where(t => t.User.Username.ToLower().Contains(searchClean) || 
                                     t.TransactionId.ToString() == searchClean || 
                                     (t.ReferenceCode != null && t.ReferenceCode.ToLower().Contains(searchClean)));
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "ALL")
        {
            query = query.Where(t => t.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(type) && type != "ALL")
        {
            query = query.Where(t => t.Type == type);
        }

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.StartedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(t => MapToDto(t, t.User.Username)).ToList();
        return new PagedResult<TransactionDto>(dtos, totalCount, pageNumber, pageSize);
    }

    public async Task<PagedResult<TransactionDto>> GetUserTransactionsPaginatedAsync(int userId, int pageNumber, int pageSize)
    {
        var query = _dbContext.Transactions
            .Include(t => t.User)
            .Include(t => t.Approver)
            .Where(t => t.UserId == userId);

        var totalCount = await query.CountAsync();
        var items = await query
            .OrderByDescending(t => t.StartedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(t => MapToDto(t, t.User.Username)).ToList();
        return new PagedResult<TransactionDto>(dtos, totalCount, pageNumber, pageSize);
    }

    public async Task<bool> CreateTransactionAsync(int userId, CreateTransactionDto dto)
    {
        if (dto.Amount <= 0 || dto.Amount > int.MaxValue || decimal.Truncate(dto.Amount) != dto.Amount)
            return false;
        if (!"DEPOSIT".Equals(dto.Type?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;

        var tx = new Transaction
        {
            UserId = userId,
            Amount = dto.Amount,
            Type = "DEPOSIT",
            Status = "PENDING",
            ReferenceCode = dto.ReferenceCode?.Trim(),
            BankId = dto.BankId?.Trim(),
            StartedAt = _clock.Now
        };

        _dbContext.Transactions.Add(tx);
        await _dbContext.SaveChangesAsync();
        var username = await _dbContext.Users.Where(u => u.UserId == userId).Select(u => u.Username).FirstAsync();
        var admins = await _dbContext.Users.Where(u => u.Role == "ADMIN" && u.Status == "ACTIVE").Select(u => u.UserId).ToListAsync();
        _dbContext.ModerationNotices.AddRange(admins.Select(adminId => new ModerationNotice
        {
            UserId = adminId,
            TransactionId = tx.TransactionId,
            Type = "TRANSACTION_PENDING",
            Title = "Giao dịch mới cần duyệt",
            Message = $"Giao dịch #{tx.TransactionId} của {username}: {tx.Amount:N0}đ đang chờ phê duyệt.",
            ActionUrl = $"/admin?tab=transactions&q={tx.TransactionId}&status=PENDING",
            IsRead = false,
            CreatedAt = _clock.Now
        }));
        if (admins.Count > 0)
            await _dbContext.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateTransactionStatusAsync(int transactionId, string newStatus, int adminId, string? failureReason)
    {
        newStatus = newStatus.ToUpper();
        if (newStatus != "SUCCESS" && newStatus != "CANCELLED")
            return false;

        // Idempotency: Kiểm tra trạng thái giao dịch
        var tx = await _dbContext.Transactions.FindAsync(transactionId);
        if (tx == null || tx.Status != "PENDING")
            return false; // Đã được xử lý trước đó hoặc không tồn tại

        using var dbTransaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            tx.Status = newStatus;
            tx.CompletedAt = _clock.Now;
            tx.ApproverId = adminId;
            tx.FailureReason = failureReason;

            if (newStatus == "SUCCESS" && tx.Type == "DEPOSIT")
            {
                // Concurrency Control: Cập nhật số dư với cơ chế OCC
                var balanceResult = await UpdateUserBalanceWithConcurrencyCheckAsync(tx.UserId, tx.Amount);
                if (!balanceResult.Success)
                {
                    throw new InvalidOperationException("Không thể cập nhật số dư người dùng do lỗi hệ thống.");
                }

                // Ghi sổ cái (Ledger)
                await CreateLedgerEntryAsync(tx.UserId, tx.TransactionId, tx.Amount, balanceResult.PrevBalance, balanceResult.CurrBalance, "DEPOSIT", $"Nạp tiền qua giao dịch #{tx.TransactionId}");
            }

            _dbContext.ModerationNotices.Add(new ModerationNotice
            {
                UserId = tx.UserId,
                TransactionId = tx.TransactionId,
                Type = "TRANSACTION_RESOLVED",
                Title = newStatus == "SUCCESS" ? "Giao dịch đã hoàn thành" : "Giao dịch đã bị hủy",
                Message = $"Mã giao dịch: #{tx.TransactionId}\nSố tiền: {tx.Amount:N0}đ\nTrạng thái: {newStatus}\nNgày hoàn thành: {tx.CompletedAt:dd/MM/yyyy HH:mm}" + 
                          (newStatus == "CANCELLED" && !string.IsNullOrWhiteSpace(failureReason) ? $"\nLý do từ chối: {failureReason}" : ""),
                ActionUrl = "/wallet",
                IsRead = false,
                CreatedAt = _clock.Now
            });

            await _dbContext.SaveChangesAsync();
            await dbTransaction.CommitAsync();
            return true;
        }
        catch (Exception)
        {
            await dbTransaction.RollbackAsync();
            return false;
        }
    }

    public async Task<bool> RefundTransactionAsync(int transactionId, int adminId, string? reason)
    {
        var originTx = await _dbContext.Transactions.FindAsync(transactionId);
        if (originTx == null || originTx.Status != "SUCCESS")
            return false; // Giao dịch gốc phải thành công mới được hoàn tiền

        // Không cho hoàn tiền của chính giao dịch hoàn tiền hoặc giao dịch không trừ ví
        if (originTx.Type == "REFUND")
            return false;

        // Kiểm tra xem giao dịch này đã được hoàn tiền trước đó chưa
        bool alreadyRefunded = await _dbContext.Transactions
            .AnyAsync(t => t.Type == "REFUND" && t.ReferenceCode == originTx.TransactionId.ToString() && t.Status == "SUCCESS");
        if (alreadyRefunded)
            return false;

        decimal refundAmount = Math.Abs(originTx.Amount); // Số tiền cần hoàn trả (luôn dương)

        using var dbTransaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            // 1. Tạo giao dịch REFUND mới
            var refundTx = new Transaction
            {
                UserId = originTx.UserId,
                Amount = refundAmount,
                Type = "REFUND",
                Status = "SUCCESS",
                ReferenceCode = originTx.TransactionId.ToString(), // Lưu lại TransactionId gốc làm Reference Code
                ApproverId = adminId,
                StartedAt = _clock.Now,
                CompletedAt = _clock.Now,
                FailureReason = reason
            };
            _dbContext.Transactions.Add(refundTx);
            await _dbContext.SaveChangesAsync();

            // 2. Cộng lại tiền cho User với cơ chế Concurrency control
            var balanceResult = await UpdateUserBalanceWithConcurrencyCheckAsync(originTx.UserId, refundAmount);
            if (!balanceResult.Success)
            {
                throw new InvalidOperationException("Không thể hoàn tiền do cập nhật số dư thất bại.");
            }

            // 3. Ghi Sổ cái (Ledger)
            await CreateLedgerEntryAsync(originTx.UserId, refundTx.TransactionId, refundAmount, balanceResult.PrevBalance, balanceResult.CurrBalance, "REFUND", $"Hoàn tiền giao dịch #{originTx.TransactionId}. Lý do: {reason}");

            // 4. Nếu giao dịch bị hoàn là mua Premium (WITHDRAW), ta hạ cấp user về gói Free và ghi lịch sử subscription
            if (originTx.Type == "WITHDRAW")
            {
                var user = await _dbContext.Users.FindAsync(originTx.UserId);
                if (user != null && user.TierId == 3)
                {
                    int oldTier = user.TierId ?? 2;
                    user.TierId = 2; // Hạ cấp về Free
                    user.ExpiresAt = null;
                    user.GracePeriodEndsAt = null;

                    var subHistory = new SubscriptionHistory
                    {
                        UserId = user.UserId,
                        OldTierId = oldTier,
                        NewTierId = 2,
                        ChangeReason = "REFUND_CANCEL",
                        ChangedAt = _clock.Now
                    };
                    _dbContext.SubscriptionHistories.Add(subHistory);
                }
            }

            _dbContext.ModerationNotices.Add(new ModerationNotice
            {
                UserId = originTx.UserId,
                TransactionId = refundTx.TransactionId,
                Type = "TRANSACTION_RESOLVED",
                Title = "Bạn được hoàn tiền",
                Message = $"Bạn đã được hoàn lại số tiền {refundAmount:N0}đ từ giao dịch #{originTx.TransactionId}.\nLý do: {reason}",
                ActionUrl = "/wallet",
                IsRead = false,
                CreatedAt = _clock.Now
            });

            await _dbContext.SaveChangesAsync();
            await dbTransaction.CommitAsync();
            return true;
        }
        catch (Exception)
        {
            await dbTransaction.RollbackAsync();
            return false;
        }
    }

    public async Task<bool> BuyPremiumAsync(int userId)
    {
        var sTier = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.TierId == 3); // Premium
        if (sTier == null)
        {
            throw new InvalidOperationException("Gói thành viên Premium chưa được cấu hình trong hệ thống.");
        }
        decimal premiumCost = sTier.Price ?? throw new InvalidOperationException("Giá của gói Premium chưa được thiết lập.");

        using var dbTransaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
                return false;

            if ((user.Balance ?? 0) < premiumCost)
            {
                throw new InvalidOperationException("Số dư không đủ để đăng ký Premium.");
            }

            int oldTierId = user.TierId ?? 2;

            // 1. Khấu trừ số dư với Concurrency control (trừ tiền)
            var balanceResult = await UpdateUserBalanceWithConcurrencyCheckAsync(userId, -premiumCost);
            if (!balanceResult.Success)
            {
                throw new InvalidOperationException("Lỗi xử lý tài chính.");
            }

            // 2. Tạo giao dịch WITHDRAW thành công
            var tx = new Transaction
            {
                UserId = userId,
                Amount = -premiumCost,
                Type = "WITHDRAW",
                Status = "SUCCESS",
                StartedAt = _clock.Now,
                CompletedAt = _clock.Now
            };
            _dbContext.Transactions.Add(tx);
            await _dbContext.SaveChangesAsync();

            // 3. Ghi Sổ cái (Ledger)
            await CreateLedgerEntryAsync(userId, tx.TransactionId, -premiumCost, balanceResult.PrevBalance, balanceResult.CurrBalance, "WITHDRAW", $"Đăng ký gói Premium hạn 30 ngày");

            // 4. Cập nhật Subscription trên User
            user.TierId = 3; // Premium
            user.ExpiresAt = _clock.Now.AddDays(30);
            user.ExpiryNotified = false;
            user.GracePeriodEndsAt = null;

            // 5. Ghi nhận Lịch sử Subscription
            var subHistory = new SubscriptionHistory
            {
                UserId = userId,
                OldTierId = oldTierId,
                NewTierId = 3,
                ChangeReason = "USER_BUY",
                ChangedAt = _clock.Now
            };
            _dbContext.SubscriptionHistories.Add(subHistory);

            await _dbContext.SaveChangesAsync();
            await dbTransaction.CommitAsync();
            return true;
        }
        catch (Exception)
        {
            await dbTransaction.RollbackAsync();
            return false;
        }
    }

    public async Task<List<SubscriptionDto>> GetSubscriptionTiersAsync()
    {
        var tiers = await _dbContext.Subscriptions.ToListAsync();
        return tiers.Select(s => new SubscriptionDto
        {
            TierId = s.TierId,
            TierName = s.TierName,
            MaxStorageMb = s.MaxStorageMb,
            AiPromptLimitPerDay = s.AiPromptLimitPerDay,
            Price = s.Price ?? 0.00m,
            TotalStorageMb = s.TotalStorageMb
        }).ToList();
    }

    #region Helper Methods

    private async Task<(bool Success, decimal PrevBalance, decimal CurrBalance)> UpdateUserBalanceWithConcurrencyCheckAsync(int userId, decimal amountChange)
    {
        int retries = 3;
        while (retries > 0)
        {
            try
            {
                var user = await _dbContext.Users.FindAsync(userId);
                if (user == null) return (false, 0, 0);

                decimal prevBalance = user.Balance ?? 0;
                decimal currBalance = prevBalance + amountChange;
                if (currBalance < 0) return (false, 0, 0); // Invalid balance

                user.Balance = (int)currBalance;
                user.BalanceVersion += 1; // Tăng version cho OCC
                await _dbContext.SaveChangesAsync();
                return (true, prevBalance, currBalance);
            }
            catch (DbUpdateConcurrencyException)
            {
                retries--;
                if (retries == 0) throw;
                // Reload entity
                var u = await _dbContext.Users.FindAsync(userId);
                if (u != null && _dbContext is DbContext efContext)
                {
                    efContext.Entry(u).State = EntityState.Detached;
                }
            }
        }
        return (false, 0, 0);
    }

    private async Task CreateLedgerEntryAsync(int userId, int? transactionId, decimal amount, decimal prevBalance, decimal currBalance, string actionType, string? description)
    {
        var ledger = new BalanceLedger
        {
            UserId = userId,
            TransactionId = transactionId,
            Amount = amount,
            PreviousBalance = prevBalance,
            CurrentBalance = currBalance,
            ActionType = actionType,
            Description = description,
            CreatedAt = _clock.Now,
            Signature = ""
        };

        // Tính chữ ký Signature = SHA256 of "UserId|Amount|PreviousBalance|CurrentBalance|ActionType|CreatedAt|LedgerSecretKey"
        string input = $"{userId}|{amount:F2}|{prevBalance:F2}|{currBalance:F2}|{actionType}|{ledger.CreatedAt:yyyy-MM-dd HH:mm:ss}|{LedgerSecretKey}";
        using (var sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            ledger.Signature = Convert.ToHexString(bytes).ToLower();
        }

        _dbContext.BalanceLedgers.Add(ledger);
        await _dbContext.SaveChangesAsync();
    }

    private TransactionDto MapToDto(Transaction t, string username)
    {
        return new TransactionDto
        {
            TransactionId = t.TransactionId,
            UserId = t.UserId,
            Username = username,
            Amount = t.Amount,
            Type = t.Type ?? "DEPOSIT",
            Status = t.Status ?? "PENDING",
            StartedAt = t.StartedAt,
            CompletedAt = t.CompletedAt,
            ReferenceCode = t.ReferenceCode,
            BankId = t.BankId,
            ApproverId = t.ApproverId,
            ApproverName = t.Approver != null ? t.Approver.Username : null,
            FailureReason = t.FailureReason
        };
    }

    #endregion
}
