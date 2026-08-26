using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly IBalanceLedgerService _ledgerService;
    private readonly ISubscriptionPurchaseService _subscriptionPurchaseService;

    public TransactionService(
        IStudyHubDbContext dbContext,
        IClock clock,
        IBalanceLedgerService ledgerService,
        ISubscriptionPurchaseService subscriptionPurchaseService)
    {
        _dbContext = dbContext;
        _clock = clock;
        _ledgerService = ledgerService;
        _subscriptionPurchaseService = subscriptionPurchaseService;
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

    public async Task<PagedResult<TransactionDto>> GetTransactionsPaginatedAsync(int pageNumber, int pageSize, string? search, string? status, string? type, DateTime? startDate = null, DateTime? endDate = null)
    {
        var query = _dbContext.Transactions
            .Include(t => t.User)
            .Include(t => t.Approver)
            .AsQueryable();

        // Apply Date Filters
        if (startDate.HasValue)
        {
            query = query.Where(t => t.StartedAt >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            var exclusiveEnd = endDate.Value.Date.AddDays(1);
            query = query.Where(t => t.StartedAt < exclusiveEnd);
        }

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
            .ThenByDescending(t => t.TransactionId)
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
            .ThenByDescending(t => t.TransactionId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var dtos = items.Select(t => MapToDto(t, t.User.Username)).ToList();
        return new PagedResult<TransactionDto>(dtos, totalCount, pageNumber, pageSize);
    }

    public async Task<bool> CreateTransactionAsync(int userId, CreateTransactionDto dto)
    {
        if (dto.Amount < 2_000 || dto.Amount > int.MaxValue || decimal.Truncate(dto.Amount) != dto.Amount)
            return false;
        if (!"DEPOSIT".Equals(dto.Type?.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(dto.BankId) || string.IsNullOrWhiteSpace(dto.ReferenceCode))
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

        using var dbTransaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            // Claim the command atomically. Only one concurrent request can move PENDING
            // to a terminal state; losers return Conflict through the controller.
            var completedAt = _clock.Now;
            var affectedRows = await _dbContext.Transactions
                .Where(t => t.TransactionId == transactionId && t.Status == "PENDING")
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.Status, newStatus)
                    .SetProperty(t => t.CompletedAt, completedAt)
                    .SetProperty(t => t.ApproverId, adminId)
                    .SetProperty(t => t.FailureReason, failureReason));
            if (affectedRows != 1)
            {
                await dbTransaction.RollbackAsync();
                return false;
            }

            var tx = await _dbContext.Transactions.FindAsync(transactionId)
                ?? throw new InvalidOperationException("Giao dịch vừa được claim nhưng không thể tải lại.");

            if (newStatus == "SUCCESS" && tx.Type == "DEPOSIT")
            {
                // Concurrency Control: Cập nhật số dư với cơ chế OCC
                var balanceResult = await UpdateUserBalanceWithConcurrencyCheckAsync(tx.UserId, tx.Amount);
                if (!balanceResult.Success)
                {
                    throw new InvalidOperationException("Không thể cập nhật số dư người dùng do lỗi hệ thống.");
                }

                // Ghi sổ cái (Ledger) qua IBalanceLedgerService (HMAC-SHA256 hash chain)
                await _ledgerService.AppendEntryAsync(tx.UserId, tx.TransactionId, tx.Amount, balanceResult.PrevBalance, balanceResult.CurrBalance, "DEPOSIT", $"Nạp tiền qua giao dịch #{tx.TransactionId}");
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

        // Refund is a credit that reverses a purchase/withdrawal.
        if (originTx.Type != "WITHDRAW" && originTx.Type != "PURCHASE")
            return false;

        // Kiểm tra xem giao dịch này đã được hoàn tiền trước đó chưa (qua OriginalTransactionId hoặc fallback ReferenceCode)
        bool alreadyRefunded = await _dbContext.Transactions
            .AnyAsync(t => (t.OriginalTransactionId == originTx.TransactionId || (t.Type == "REFUND" && t.ReferenceCode == originTx.TransactionId.ToString())) && t.Status == "SUCCESS");
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
                Type = "REFUND_PURCHASE",
                Status = "SUCCESS",
                OriginalTransactionId = originTx.TransactionId,
                ReferenceCode = originTx.TransactionId.ToString(),
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

            // 3. Ghi Sổ cái (Ledger) HMAC-SHA256
            await _ledgerService.AppendEntryAsync(originTx.UserId, refundTx.TransactionId, refundAmount, balanceResult.PrevBalance, balanceResult.CurrBalance, "REFUND_PURCHASE", $"Hoàn tiền mua gói #{originTx.TransactionId}. Lý do: {reason}");

            // 4. Khôi phục gói dịch vụ trước đó của user
            var user = await _dbContext.Users.FindAsync(originTx.UserId);
            if (user != null && user.TierId == 3)
            {
                var lastHistory = await _dbContext.SubscriptionHistories
                    .Where(h => h.UserId == user.UserId && h.NewTierId == 3)
                    .OrderByDescending(h => h.ChangedAt)
                    .FirstOrDefaultAsync();

                int restoreTier = lastHistory?.OldTierId ?? 2;
                await _subscriptionPurchaseService.RecordRefundCancelAsync(user.UserId, originTx.TransactionId, restoreTier, "REFUND_CANCEL");
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

    public async Task<bool> ReverseDepositAsync(int transactionId, int adminId, string reason)
    {
        var originTx = await _dbContext.Transactions.FindAsync(transactionId);
        if (originTx == null || originTx.Status != "SUCCESS" || originTx.Type != "DEPOSIT")
            return false;

        // Check if already reversed
        bool alreadyReversed = await _dbContext.Transactions
            .AnyAsync(t => t.OriginalTransactionId == originTx.TransactionId && t.Status == "SUCCESS");
        if (alreadyReversed)
            return false;

        var user = await _dbContext.Users.FindAsync(originTx.UserId);
        if (user == null)
            return false;

        decimal debitAmount = originTx.Amount; // Positive value of deposit
        if ((user.Balance ?? 0) < debitAmount)
        {
            // Cannot reverse deposit if user does not have sufficient balance
            return false;
        }

        using var dbTransaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            // 1. Create REVERSE_DEPOSIT transaction
            var reverseTx = new Transaction
            {
                UserId = originTx.UserId,
                Amount = -debitAmount,
                Type = "REVERSE_DEPOSIT",
                Status = "SUCCESS",
                OriginalTransactionId = originTx.TransactionId,
                ReferenceCode = originTx.TransactionId.ToString(),
                ApproverId = adminId,
                StartedAt = _clock.Now,
                CompletedAt = _clock.Now,
                FailureReason = reason
            };
            _dbContext.Transactions.Add(reverseTx);
            await _dbContext.SaveChangesAsync();

            // 2. Debit balance with OCC concurrency
            var balanceResult = await UpdateUserBalanceWithConcurrencyCheckAsync(originTx.UserId, -debitAmount);
            if (!balanceResult.Success)
            {
                throw new InvalidOperationException("Khấu trừ số dư thất bại khi đảo giao dịch nạp tiền.");
            }

            // 3. Append Ledger entry
            await _ledgerService.AppendEntryAsync(originTx.UserId, reverseTx.TransactionId, -debitAmount, balanceResult.PrevBalance, balanceResult.CurrBalance, "REVERSE_DEPOSIT", $"Thu hồi nạp tiền giao dịch #{originTx.TransactionId}. Lý do: {reason}");

            _dbContext.ModerationNotices.Add(new ModerationNotice
            {
                UserId = originTx.UserId,
                TransactionId = reverseTx.TransactionId,
                Type = "TRANSACTION_RESOLVED",
                Title = "Giao dịch nạp tiền đã bị thu hồi",
                Message = $"Giao dịch nạp tiền #{originTx.TransactionId} ({debitAmount:N0}đ) đã bị thu hồi.\nLý do: {reason}",
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
        var result = await _subscriptionPurchaseService.PurchaseTierAsync(userId, 3);
        return result.Success;
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
            FailureReason = t.FailureReason,
            OriginalTransactionId = t.OriginalTransactionId
        };
    }

    #endregion
}
