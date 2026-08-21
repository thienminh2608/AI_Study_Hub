using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Application.Services;

public class SubscriptionPurchaseService : ISubscriptionPurchaseService
{
    private readonly IStudyHubDbContext _dbContext;
    private readonly IBalanceLedgerService _ledgerService;
    private readonly IClock _clock;
    private readonly ILogger<SubscriptionPurchaseService> _logger;

    public SubscriptionPurchaseService(
        IStudyHubDbContext dbContext,
        IBalanceLedgerService ledgerService,
        IClock clock,
        ILogger<SubscriptionPurchaseService> logger)
    {
        _dbContext = dbContext;
        _ledgerService = ledgerService;
        _clock = clock;
        _logger = logger;
    }

    public async Task<SubscriptionPurchaseResultDto> PurchaseTierAsync(int userId, int targetTierId, CancellationToken cancellationToken = default)
    {
        using var dbTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
            if (user == null)
            {
                return new SubscriptionPurchaseResultDto { Success = false, Message = "Không tìm thấy người dùng." };
            }

            var targetTier = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.TierId == targetTierId, cancellationToken);
            if (targetTier == null)
            {
                return new SubscriptionPurchaseResultDto { Success = false, Message = "Gói đăng ký không tồn tại." };
            }

            decimal price = targetTier.Price ?? 0m;
            int oldTierId = user.TierId ?? 2;
            Transaction? tx = null;
            decimal prevBalance = (decimal)(user.Balance ?? 0);
            decimal currBalance = prevBalance;

            if (price > 0)
            {
                if (prevBalance < price)
                {
                    await dbTransaction.RollbackAsync(cancellationToken);
                    return new SubscriptionPurchaseResultDto
                    {
                        Success = false,
                        Message = "Số dư trong ví không đủ để thực hiện nâng cấp."
                    };
                }

                currBalance = prevBalance - price;
                user.Balance = (int)currBalance;
                user.BalanceVersion += 1;
                user.UpdatedAt = _clock.Now;

                tx = new Transaction
                {
                    UserId = userId,
                    Amount = -price,
                    Type = "WITHDRAW",
                    Status = "SUCCESS",
                    StartedAt = _clock.Now,
                    CompletedAt = _clock.Now
                };
                _dbContext.Transactions.Add(tx);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _ledgerService.AppendEntryAsync(
                    userId,
                    tx.TransactionId,
                    -price,
                    prevBalance,
                    currBalance,
                    "PURCHASE",
                    $"Đăng ký gói {targetTier.TierName} hạn 30 ngày",
                    cancellationToken);
            }

            // Update user subscription state
            user.TierId = targetTier.TierId;
            user.ExpiresAt = _clock.Now.AddDays(30);
            user.GracePeriodEndsAt = null;
            user.ExpiryNotified = false;

            // Record immutable snapshot
            var subHistory = new SubscriptionHistory
            {
                UserId = userId,
                TransactionId = tx?.TransactionId,
                OldTierId = oldTierId,
                NewTierId = targetTier.TierId,
                TierNameSnapshot = targetTier.TierName,
                PriceSnapshot = price,
                CurrencySnapshot = "VND",
                DurationDaysSnapshot = 30,
                StorageLimitSnapshot = targetTier.MaxStorageMb,
                AiPromptLimitSnapshot = targetTier.AiPromptLimitPerDay,
                PricingPolicySnapshot = "INITIAL_PURCHASE_PRICE",
                PurchaseType = "INITIAL_PURCHASE",
                ChangeReason = "USER_BUY",
                ChangedAt = _clock.Now,
                PurchasedAt = _clock.Now,
                EffectiveFrom = _clock.Now,
                EffectiveUntil = user.ExpiresAt
            };
            _dbContext.SubscriptionHistories.Add(subHistory);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await dbTransaction.CommitAsync(cancellationToken);

            _logger.LogInformation("User {UserId} successfully purchased tier {TierName} with snapshot price {Price}", userId, targetTier.TierName, price);

            return new SubscriptionPurchaseResultDto
            {
                Success = true,
                Message = $"Đăng ký gói {targetTier.TierName} thành công!",
                HistoryId = subHistory.HistoryId,
                TransactionId = tx?.TransactionId,
                TierName = targetTier.TierName,
                PricePaid = price,
                EffectiveUntil = user.ExpiresAt
            };
        }
        catch (Exception ex)
        {
            await dbTransaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to purchase tier {TierId} for user {UserId}", targetTierId, userId);
            return new SubscriptionPurchaseResultDto
            {
                Success = false,
                Message = "Lỗi khi xử lý giao dịch mua gói."
            };
        }
    }

    public async Task<bool> AutoRenewSubscriptionAsync(int userId, int targetTierId, CancellationToken cancellationToken = default)
    {
        using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
            if (user == null) return false;

            var targetTier = await _dbContext.Subscriptions.FirstOrDefaultAsync(s => s.TierId == targetTierId, cancellationToken);
            if (targetTier == null) return false;

            decimal currentPrice = targetTier.Price ?? 0m;
            decimal prevBalance = (decimal)(user.Balance ?? 0);
            if (prevBalance < currentPrice)
            {
                await tx.RollbackAsync(cancellationToken);
                return false;
            }

            decimal currBalance = prevBalance - currentPrice;
            user.Balance = (int)currBalance;
            user.BalanceVersion += 1;
            user.UpdatedAt = _clock.Now;

            var transaction = new Transaction
            {
                UserId = userId,
                Amount = -currentPrice,
                Type = "WITHDRAW",
                Status = "SUCCESS",
                StartedAt = _clock.Now,
                CompletedAt = _clock.Now
            };
            _dbContext.Transactions.Add(transaction);
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _ledgerService.AppendEntryAsync(
                userId,
                transaction.TransactionId,
                -currentPrice,
                prevBalance,
                currBalance,
                "PURCHASE",
                $"Tự động gia hạn gói {targetTier.TierName} 30 ngày",
                cancellationToken);

            // Extend expiration date
            DateTime fromDate = (user.ExpiresAt.HasValue && user.ExpiresAt.Value > _clock.Now) ? user.ExpiresAt.Value : _clock.Now;
            user.TierId = targetTier.TierId;
            user.ExpiresAt = fromDate.AddDays(30);
            user.GracePeriodEndsAt = null;
            user.ExpiryNotified = false;

            // Record renewal snapshot with STANDARD_CURRENT_TIER_PRICE
            var subHistory = new SubscriptionHistory
            {
                UserId = userId,
                TransactionId = transaction.TransactionId,
                OldTierId = targetTier.TierId,
                NewTierId = targetTier.TierId,
                TierNameSnapshot = targetTier.TierName,
                PriceSnapshot = currentPrice,
                CurrencySnapshot = "VND",
                DurationDaysSnapshot = 30,
                StorageLimitSnapshot = targetTier.MaxStorageMb,
                AiPromptLimitSnapshot = targetTier.AiPromptLimitPerDay,
                PricingPolicySnapshot = "STANDARD_CURRENT_TIER_PRICE",
                PurchaseType = "AUTO_RENEW",
                ChangeReason = "AUTO_RENEW_SUCCESS",
                ChangedAt = _clock.Now,
                PurchasedAt = _clock.Now,
                EffectiveFrom = fromDate,
                EffectiveUntil = user.ExpiresAt
            };
            _dbContext.SubscriptionHistories.Add(subHistory);

            await _dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation("Auto-renewed subscription for User {UserId} with current price {Price}", userId, currentPrice);
            return true;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to auto-renew subscription for User {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> DowngradeUserAsync(int userId, string reason, CancellationToken cancellationToken = default)
    {
        using var tx = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
            if (user == null) return false;

            int oldTier = user.TierId ?? 3;
            user.TierId = 2; // Free
            user.ExpiresAt = null;
            user.GracePeriodEndsAt = null;
            user.ExpiryNotified = false;
            user.DowngradeNoticePending = true;

            var subHistory = new SubscriptionHistory
            {
                UserId = userId,
                OldTierId = oldTier,
                NewTierId = 2,
                TierNameSnapshot = "FREE",
                PriceSnapshot = 0m,
                CurrencySnapshot = "VND",
                DurationDaysSnapshot = 0,
                PricingPolicySnapshot = "DOWNGRADE_POLICY",
                PurchaseType = "DOWNGRADE",
                ChangeReason = reason,
                ChangedAt = _clock.Now,
                PurchasedAt = _clock.Now,
                EffectiveFrom = _clock.Now,
                EffectiveUntil = null
            };
            _dbContext.SubscriptionHistories.Add(subHistory);

            _dbContext.ModerationNotices.Add(new ModerationNotice
            {
                UserId = userId,
                Type = "SUBSCRIPTION_DOWNGRADED",
                Title = "Tài khoản của bạn đã bị hạ cấp",
                Message = "Gói Premium của bạn đã bị hủy hoặc hết hạn ân hạn mà số dư không đủ. Tài khoản đã chuyển về gói Free.",
                ActionUrl = "/premium",
                IsRead = false,
                CreatedAt = _clock.Now
            });

            await _dbContext.SaveChangesAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);

            _logger.LogInformation("User {UserId} downgraded to Free tier. Reason: {Reason}", userId, reason);
            return true;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to downgrade user {UserId}", userId);
            return false;
        }
    }

    public async Task<bool> RecordRefundCancelAsync(int userId, int originTxId, int restoreTierId, string reason, CancellationToken cancellationToken = default)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user == null) return false;

        int oldTierId = user.TierId ?? 3;
        user.TierId = restoreTierId;
        user.ExpiresAt = null;
        user.GracePeriodEndsAt = null;

        var subHistory = new SubscriptionHistory
        {
            UserId = userId,
            TransactionId = originTxId,
            OldTierId = oldTierId,
            NewTierId = restoreTierId,
            TierNameSnapshot = "REFUNDED",
            PriceSnapshot = 0m,
            CurrencySnapshot = "VND",
            DurationDaysSnapshot = 0,
            PricingPolicySnapshot = "REFUND_CANCEL_POLICY",
            PurchaseType = "REFUND_CANCEL",
            ChangeReason = reason,
            ChangedAt = _clock.Now,
            PurchasedAt = _clock.Now,
            EffectiveFrom = _clock.Now,
            EffectiveUntil = null
        };
        _dbContext.SubscriptionHistories.Add(subHistory);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PagedResult<SubscriptionHistoryDto>> GetUserSubscriptionHistoryAsync(int userId, int pageNumber, int pageSize, CancellationToken cancellationToken = default)
    {
        pageNumber = Math.Max(1, pageNumber);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _dbContext.SubscriptionHistories.AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.ChangedAt);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(h => new SubscriptionHistoryDto
            {
                HistoryId = h.HistoryId,
                UserId = h.UserId,
                TransactionId = h.TransactionId,
                OldTierId = h.OldTierId,
                NewTierId = h.NewTierId,
                TierNameSnapshot = h.TierNameSnapshot,
                PriceSnapshot = h.PriceSnapshot,
                CurrencySnapshot = h.CurrencySnapshot,
                DurationDaysSnapshot = h.DurationDaysSnapshot,
                StorageLimitSnapshot = h.StorageLimitSnapshot,
                AiPromptLimitSnapshot = h.AiPromptLimitSnapshot,
                PricingPolicySnapshot = h.PricingPolicySnapshot,
                PurchaseType = h.PurchaseType,
                ChangeReason = h.ChangeReason,
                ChangedAt = h.ChangedAt,
                PurchasedAt = h.PurchasedAt,
                EffectiveFrom = h.EffectiveFrom,
                EffectiveUntil = h.EffectiveUntil
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<SubscriptionHistoryDto>
        {
            Items = items,
            TotalCount = total,
            PageNumber = pageNumber,
            PageSize = pageSize
        };
    }
}
