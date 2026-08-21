using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface ISubscriptionPurchaseService
{
    Task<SubscriptionPurchaseResultDto> PurchaseTierAsync(int userId, int targetTierId, CancellationToken cancellationToken = default);
    Task<bool> AutoRenewSubscriptionAsync(int userId, int targetTierId, CancellationToken cancellationToken = default);
    Task<bool> DowngradeUserAsync(int userId, string reason, CancellationToken cancellationToken = default);
    Task<bool> RecordRefundCancelAsync(int userId, int originTxId, int restoreTierId, string reason, CancellationToken cancellationToken = default);
    Task<PagedResult<SubscriptionHistoryDto>> GetUserSubscriptionHistoryAsync(int userId, int pageNumber, int pageSize, CancellationToken cancellationToken = default);
}
