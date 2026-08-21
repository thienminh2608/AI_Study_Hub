using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Infrastructure.Services;

public class PaymentReconciliationHostedService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PaymentReconciliationHostedService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(60);

    public PaymentReconciliationHostedService(
        IServiceProvider serviceProvider,
        ILogger<PaymentReconciliationHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PaymentReconciliationHostedService started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcilePendingTransactionsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error occurred during payment reconciliation execution cycle.");
            }

            try
            {
                await Task.Delay(_checkInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("PaymentReconciliationHostedService stopped.");
    }

    public async Task ReconcilePendingTransactionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IStudyHubDbContext>();
        var payOsService = scope.ServiceProvider.GetRequiredService<IPayOsService>();
        var completionService = scope.ServiceProvider.GetRequiredService<IPaymentCompletionService>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        var staleThreshold = now.AddMinutes(-5); // Transactions older than 5 minutes
        var leaseDuration = TimeSpan.FromMinutes(3);

        // Claim candidate transactions with atomic conditional update
        var candidateIds = await db.Transactions
            .Where(t => t.PayOsOrderCode != null &&
                        (t.Status == "PENDING" || t.Status == "CREATING") &&
                        t.StartedAt < staleThreshold &&
                        (t.ReconciliationLockedUntil == null || t.ReconciliationLockedUntil < now) &&
                        t.ReconciliationAttempts < 10)
            .OrderBy(t => t.StartedAt)
            .Take(10)
            .Select(t => new { t.TransactionId, PayOsOrderCode = t.PayOsOrderCode!.Value, t.Amount })
            .ToListAsync(cancellationToken);

        if (!candidateIds.Any())
        {
            return;
        }

        foreach (var candidate in candidateIds)
        {
            if (cancellationToken.IsCancellationRequested) break;

            // Direct conditional claim
            var claimed = await db.Transactions
                .Where(t => t.TransactionId == candidate.TransactionId &&
                            (t.ReconciliationLockedUntil == null || t.ReconciliationLockedUntil < now) &&
                            (t.Status == "PENDING" || t.Status == "CREATING"))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.ReconciliationLockedUntil, now.Add(leaseDuration))
                    .SetProperty(t => t.ReconciliationAttempts, t => t.ReconciliationAttempts + 1)
                    .SetProperty(t => t.LastReconciliationAt, now),
                    cancellationToken);

            if (claimed != 1)
            {
                // Already claimed by another worker instance
                continue;
            }

            try
            {
                long orderCode = candidate.PayOsOrderCode;
                var providerInfo = await payOsService.GetPaymentRequestAsync(orderCode, cancellationToken);

                if (providerInfo == null)
                {
                    _logger.LogDebug("No payment request info found for order #{OrderCode}", orderCode);
                    await db.Transactions
                        .Where(t => t.TransactionId == candidate.TransactionId)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReconciliationLockedUntil, (DateTime?)null), cancellationToken);
                    continue;
                }

                if ("PAID".Equals(providerInfo.Status, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Reconciliation found PAID order #{OrderCode} for Tx {TxId}. Completing deposit.", orderCode, candidate.TransactionId);
                    decimal finalAmount = providerInfo.AmountPaid > 0 ? providerInfo.AmountPaid : candidate.Amount;
                    await completionService.CompleteDepositDirectAsync(
                        orderCode,
                        finalAmount,
                        "VND",
                        $"RECON_{orderCode}_{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}",
                        "PAYOS_RECON",
                        cancellationToken);
                }
                else if ("CANCELLED".Equals(providerInfo.Status, StringComparison.OrdinalIgnoreCase) ||
                         "EXPIRED".Equals(providerInfo.Status, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("Reconciliation marking Tx {TxId} as {Status}.", candidate.TransactionId, providerInfo.Status);
                    await db.Transactions
                        .Where(t => t.TransactionId == candidate.TransactionId)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(t => t.Status, providerInfo.Status.ToUpperInvariant())
                            .SetProperty(t => t.FailureReason, $"PayOS payment status: {providerInfo.Status}")
                            .SetProperty(t => t.ReconciliationLockedUntil, (DateTime?)null),
                            cancellationToken);
                }
                else
                {
                    // Still pending at provider side
                    await db.Transactions
                        .Where(t => t.TransactionId == candidate.TransactionId)
                        .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReconciliationLockedUntil, (DateTime?)null), cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Reconciliation failed for Tx {TxId}", candidate.TransactionId);
                await db.Transactions
                    .Where(t => t.TransactionId == candidate.TransactionId)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.ReconciliationLockedUntil, (DateTime?)null), cancellationToken);
            }
        }
    }
}
