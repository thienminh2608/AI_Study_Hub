using System.Collections.Generic;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface ITransactionService
{
    Task<List<TransactionDto>> GetUserTransactionsAsync(int userId);
    Task<List<TransactionDto>> GetAllTransactionsAsync();
    Task<PagedResult<TransactionDto>> GetTransactionsPaginatedAsync(int pageNumber, int pageSize, string? search, string? status, string? type);
    Task<PagedResult<TransactionDto>> GetUserTransactionsPaginatedAsync(int userId, int pageNumber, int pageSize);
    Task<bool> CreateTransactionAsync(int userId, CreateTransactionDto dto);
    Task<bool> UpdateTransactionStatusAsync(int transactionId, string newStatus, int adminId, string? failureReason);
    Task<bool> RefundTransactionAsync(int transactionId, int adminId, string? reason);
    Task<bool> BuyPremiumAsync(int userId);
    Task<List<SubscriptionDto>> GetSubscriptionTiersAsync();
}
