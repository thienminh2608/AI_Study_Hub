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

        // Fetch Premium users whose subscription has expired
        var expiredUsers = await dbContext.Users
            .Where(u => u.TierId == 3 && u.ExpiresAt != null && u.ExpiresAt <= _clock.Now)
            .ToListAsync();

        foreach (var u in expiredUsers)
        {
            if ((u.Balance ?? 0) >= premiumPrice)
            {
                // Auto-renew
                using var tx = await dbContext.Database.BeginTransactionAsync();
                try
                {
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

                    // Deduct balance and extend subscription by 30 days
                    u.Balance = (u.Balance ?? 0) - (int)premiumPrice;
                    u.ExpiresAt = _clock.Now.AddDays(30);
                    u.ExpiryNotified = false;

                    await dbContext.SaveChangesAsync();
                    await tx.CommitAsync();
                    _logger.LogInformation("Successfully renewed Premium subscription for User ID {UserId}", u.UserId);
                }
                catch (Exception ex)
                {
                    await tx.RollbackAsync();
                    _logger.LogError(ex, "Failed to renew subscription for User ID {UserId}", u.UserId);
                }
            }
            else
            {
                // Downgrade to Free
                u.TierId = 2; // Free
                u.ExpiresAt = null;
                u.ExpiryNotified = false;
                u.DowngradeNoticePending = true;

                await dbContext.SaveChangesAsync();

                mailService.SendPremiumDowngraded(u.Email ?? "", u.Username);
                _logger.LogInformation("Downgraded User ID {UserId} to Free due to insufficient balance.", u.UserId);
            }
        }
    }
}
