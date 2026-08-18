using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Infrastructure.Services;

public class SubscriptionRenewalScheduler : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SubscriptionRenewalScheduler> _logger;
    private readonly IClock _clock;
    private const int WarningHoursBeforeExpiry = 72;
    private const string LedgerSecretKey = "AIStudyHubSecureLedgerKey";

    public SubscriptionRenewalScheduler(IServiceProvider serviceProvider, ILogger<SubscriptionRenewalScheduler> logger, IClock clock)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _clock = clock;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Subscription Renewal Scheduler is starting.");

        try
        {
            // Wait 5 seconds before first run
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<IStudyHubDbContext>();
                    var mailService = scope.ServiceProvider.GetRequiredService<IMailService>();

                    await ProcessExpiryWarningsAsync(dbContext, mailService);
                    await ProcessExpiredSubscriptionsAsync(dbContext, mailService);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in Subscription Renewal Scheduler.");
                }

                // Run every 60 seconds
                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }

        _logger.LogInformation("Subscription Renewal Scheduler is stopping.");
    }

    private async Task ProcessExpiryWarningsAsync(IStudyHubDbContext dbContext, IMailService mailService)
    {
        var sTier = await dbContext.Subscriptions.FirstOrDefaultAsync(s => s.TierId == 3);
        if (sTier == null || sTier.Price == null)
        {
            _logger.LogError("Gói Premium (TierId = 3) hoặc giá bán chưa được cấu hình. Bỏ qua chu kỳ quét cảnh báo.");
            return;
        }
        decimal premiumPrice = sTier.Price.Value;

        // Fetch active Premium users whose subscription expires within 72 hours, and have not been notified yet.
        var targetTime = _clock.Now.AddHours(WarningHoursBeforeExpiry);
        var soonExpiring = await dbContext.Users
            .Where(u => u.TierId == 3 && u.ExpiresAt != null && u.ExpiresAt <= targetTime && u.ExpiresAt > _clock.Now && u.ExpiryNotified == false)
            .ToListAsync();

        foreach (var u in soonExpiring)
        {
            if ((u.Balance ?? 0) < premiumPrice)
            {
                bool sent = mailService.SendPremiumExpiryWarning(u.Email ?? "", u.Username, WarningHoursBeforeExpiry);
                if (sent)
                {
                    u.ExpiryNotified = true;
                }
            }
            else
            {
                // Sufficient balance, mark notified so we don't scan it repeatedly
                u.ExpiryNotified = true;
            }
        }

        if (soonExpiring.Count > 0)
        {
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task ProcessExpiredSubscriptionsAsync(IStudyHubDbContext dbContext, IMailService mailService)
    {
        var sTier = await dbContext.Subscriptions.FirstOrDefaultAsync(s => s.TierId == 3);
        if (sTier == null || sTier.Price == null)
        {
            _logger.LogError("Gói Premium (TierId = 3) hoặc giá bán chưa được cấu hình. Bỏ qua chu kỳ quét gia hạn.");
            return;
        }
        decimal premiumPrice = sTier.Price.Value;

        // Fetch Premium users whose subscription has expired (ExpiresAt <= Now)
        var expiredUsers = await dbContext.Users
            .Where(u => u.TierId == 3 && u.ExpiresAt != null && u.ExpiresAt <= _clock.Now)
            .ToListAsync();

        foreach (var u in expiredUsers)
        {
            // Trường hợp 1: User tắt Tự động gia hạn
            if (!u.IsAutoRenew)
            {
                await DowngradeUserAsync(dbContext, mailService, u, "AUTO_RENEW_CANCELLED");
                continue;
            }

            // Trường hợp 2: Bật Tự động gia hạn
            if ((u.Balance ?? 0) >= premiumPrice)
            {
                // Auto-renew
                using var tx = await dbContext.Database.BeginTransactionAsync();
                try
                {
                    decimal prevBalance = u.Balance ?? 0;
                    decimal currBalance = prevBalance - premiumPrice;

                    // Deduct balance, increase version, and extend subscription by 30 days
                    u.Balance = (int)currBalance;
                    u.BalanceVersion += 1;
                    u.ExpiresAt = _clock.Now.AddDays(30);
                    u.ExpiryNotified = false;
                    u.GracePeriodEndsAt = null;

                    // Create SUCCESS WITHDRAW transaction
                    var transaction = new Transaction
                    {
                        UserId = u.UserId,
                        Amount = -premiumPrice,
                        Type = "WITHDRAW",
                        Status = "SUCCESS",
                        StartedAt = _clock.Now,
                        CompletedAt = _clock.Now
                    };
                    dbContext.Transactions.Add(transaction);
                    await dbContext.SaveChangesAsync();

                    // Ghi Sổ cái (Ledger)
                    await CreateLedgerEntryAsync(dbContext, u.UserId, transaction.TransactionId, -premiumPrice, prevBalance, currBalance, "WITHDRAW", "Tự động gia hạn gói Premium 30 ngày");

                    // Ghi Lịch sử Subscription
                    var subHistory = new SubscriptionHistory
                    {
                        UserId = u.UserId,
                        OldTierId = 3,
                        NewTierId = 3,
                        ChangeReason = "AUTO_RENEW_SUCCESS",
                        ChangedAt = _clock.Now
                    };
                    dbContext.SubscriptionHistories.Add(subHistory);

                    await dbContext.SaveChangesAsync();
                    await tx.CommitAsync();
                    _logger.LogInformation("Successfully auto-renewed Premium subscription for User ID {UserId}", u.UserId);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Failed to auto-renew subscription for User ID {UserId}", u.UserId);
                }
            }
            else
            {
                // Ví không đủ tiền
                if (u.GracePeriodEndsAt == null)
                {
                    // Kích hoạt Grace Period (7 ngày ân hạn)
                    u.GracePeriodEndsAt = _clock.Now.AddDays(7);
                    
                    dbContext.ModerationNotices.Add(new ModerationNotice
                    {
                        UserId = u.UserId,
                        Type = "SUBSCRIPTION_GRACE_PERIOD",
                        Title = "Premium đã hết hạn - Đang trong thời gian ân hạn",
                        Message = $"Tài khoản Premium của bạn đã hết hạn, hệ thống đã kích hoạt 7 ngày ân hạn (đến {u.GracePeriodEndsAt:dd/MM/yyyy}). Vui lòng nạp tiền vào ví để hệ thống tự động gia hạn gói.",
                        ActionUrl = "/wallet",
                        IsRead = false,
                        CreatedAt = _clock.Now
                    });

                    await dbContext.SaveChangesAsync();
                    mailService.SendPremiumExpiryWarning(u.Email ?? "", u.Username, 0); // Warning code 0 indicates grace period activation warning in custom templates
                    _logger.LogInformation("User ID {UserId} placed in 7 days grace period due to insufficient balance.", u.UserId);
                }
                else if (u.GracePeriodEndsAt <= _clock.Now)
                {
                    // Hết thời gian ân hạn -> Hạ cấp về Free
                    await DowngradeUserAsync(dbContext, mailService, u, "GRACE_PERIOD_EXPIRED");
                }
                else
                {
                    // Vẫn đang trong Grace Period, không thay đổi quyền
                    _logger.LogInformation("User ID {UserId} is currently in grace period. Expires at {GraceEnds}", u.UserId, u.GracePeriodEndsAt);
                }
            }
        }
    }

    private async Task DowngradeUserAsync(IStudyHubDbContext dbContext, IMailService mailService, User u, string reason)
    {
        using var tx = await dbContext.Database.BeginTransactionAsync();
        try
        {
            int oldTier = u.TierId ?? 3;
            u.TierId = 2; // Free
            u.ExpiresAt = null;
            u.GracePeriodEndsAt = null;
            u.ExpiryNotified = false;
            u.DowngradeNoticePending = true;

            var subHistory = new SubscriptionHistory
            {
                UserId = u.UserId,
                OldTierId = oldTier,
                NewTierId = 2,
                ChangeReason = reason,
                ChangedAt = _clock.Now
            };
            dbContext.SubscriptionHistories.Add(subHistory);

            dbContext.ModerationNotices.Add(new ModerationNotice
            {
                UserId = u.UserId,
                Type = "SUBSCRIPTION_DOWNGRADED",
                Title = "Tài khoản của bạn đã bị hạ cấp",
                Message = "Gói Premium của bạn đã bị hủy hoặc hết hạn ân hạn mà số dư không đủ. Tài khoản đã chuyển về gói Free.",
                ActionUrl = "/premium",
                IsRead = false,
                CreatedAt = _clock.Now
            });

            await dbContext.SaveChangesAsync();
            await tx.CommitAsync();

            mailService.SendPremiumDowngraded(u.Email ?? "", u.Username);
            _logger.LogInformation("Downgraded User ID {UserId} to Free. Reason: {Reason}", u.UserId, reason);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync();
            _logger.LogError(ex, "Failed to downgrade User ID {UserId}", u.UserId);
        }
    }

    private async Task CreateLedgerEntryAsync(IStudyHubDbContext dbContext, int userId, int? transactionId, decimal amount, decimal prevBalance, decimal currBalance, string actionType, string? description)
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

        // Signature = SHA256 of "UserId|Amount|PreviousBalance|CurrentBalance|ActionType|CreatedAt|LedgerSecretKey"
        string input = $"{userId}|{amount:F2}|{prevBalance:F2}|{currBalance:F2}|{actionType}|{ledger.CreatedAt:yyyy-MM-dd HH:mm:ss}|{LedgerSecretKey}";
        using (var sha = SHA256.Create())
        {
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(input));
            ledger.Signature = Convert.ToHexString(bytes).ToLower();
        }

        dbContext.BalanceLedgers.Add(ledger);
        await dbContext.SaveChangesAsync();
    }
}
