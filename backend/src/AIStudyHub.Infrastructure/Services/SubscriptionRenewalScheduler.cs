using System;
using System.Linq;
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
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var dbContext = scope.ServiceProvider.GetRequiredService<IStudyHubDbContext>();
                    var mailService = scope.ServiceProvider.GetRequiredService<IMailService>();
                    var purchaseService = scope.ServiceProvider.GetRequiredService<ISubscriptionPurchaseService>();

                    await ProcessExpiryWarningsAsync(dbContext, mailService);
                    await ProcessExpiredSubscriptionsAsync(dbContext, mailService, purchaseService);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred in Subscription Renewal Scheduler.");
                }

                await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Subscription Renewal Scheduler is stopping.");
        }
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
                u.ExpiryNotified = true;
            }
        }

        if (soonExpiring.Count > 0)
        {
            await dbContext.SaveChangesAsync();
        }
    }

    private async Task ProcessExpiredSubscriptionsAsync(IStudyHubDbContext dbContext, IMailService mailService, ISubscriptionPurchaseService purchaseService)
    {
        var expiredUsers = await dbContext.Users
            .Where(u => u.TierId == 3 && u.ExpiresAt != null && u.ExpiresAt <= _clock.Now)
            .ToListAsync();

        var premiumTier = await dbContext.Subscriptions.FirstOrDefaultAsync(s => s.TierId == 3);
        if (premiumTier == null || premiumTier.Price == null)
        {
            _logger.LogError("Gói Premium (TierId = 3) hoặc giá bán chưa được cấu hình. Bỏ qua chu kỳ quét gia hạn.");
            return;
        }
        decimal premiumPrice = premiumTier.Price.Value;

        foreach (var u in expiredUsers)
        {
            // Trường hợp 1: User tắt Tự động gia hạn
            if (!u.IsAutoRenew)
            {
                await purchaseService.DowngradeUserAsync(u.UserId, "AUTO_RENEW_CANCELLED");
                mailService.SendPremiumDowngraded(u.Email ?? "", u.Username);
                continue;
            }

            // Trường hợp 2: Bật Tự động gia hạn
            if ((u.Balance ?? 0) >= premiumPrice)
            {
                var renewed = await purchaseService.AutoRenewSubscriptionAsync(u.UserId, 3);
                if (renewed)
                {
                    _logger.LogInformation("Successfully auto-renewed Premium subscription for User ID {UserId}", u.UserId);
                }
                else
                {
                    _logger.LogError("Failed to auto-renew subscription for User ID {UserId}", u.UserId);
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
                    mailService.SendPremiumExpiryWarning(u.Email ?? "", u.Username, 0);
                    _logger.LogInformation("User ID {UserId} placed in 7 days grace period due to insufficient balance.", u.UserId);
                }
                else if (u.GracePeriodEndsAt <= _clock.Now)
                {
                    // Hết thời gian ân hạn -> Hạ cấp về Free
                    await purchaseService.DowngradeUserAsync(u.UserId, "GRACE_PERIOD_EXPIRED");
                    mailService.SendPremiumDowngraded(u.Email ?? "", u.Username);
                }
                else
                {
                    _logger.LogInformation("User ID {UserId} is currently in grace period. Expires at {GraceEnds}", u.UserId, u.GracePeriodEndsAt);
                }
            }
        }
    }
}
