using System.Collections.Generic;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;

namespace AIStudyHub.Application.Interfaces;

public interface ITransactionService
{
    Task<List<TransactionDto>> GetUserTransactionsAsync(int userId);
    Task<List<TransactionDto>> GetAllTransactionsAsync();
    Task<bool> CreateTransactionAsync(int userId, CreateTransactionDto dto);
    Task<bool> UpdateTransactionStatusAsync(int transactionId, string newStatus);
    Task<bool> BuyPremiumAsync(int userId);
    Task<List<SubscriptionDto>> GetSubscriptionTiersAsync();
}
