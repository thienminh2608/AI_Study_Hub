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

    public TransactionService(IStudyHubDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<List<TransactionDto>> GetUserTransactionsAsync(int userId)
    {
        var txs = await _dbContext.Transactions
            .Include(t => t.User)
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.StartedAt)
            .ToListAsync();

        return txs.Select(t => MapToDto(t, t.User.Username)).ToList();
    }

    public async Task<List<TransactionDto>> GetAllTransactionsAsync()
    {
        var txs = await _dbContext.Transactions
            .Include(t => t.User)
            .OrderByDescending(t => t.StartedAt)
            .ToListAsync();

        return txs.Select(t => MapToDto(t, t.User.Username)).ToList();
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

    public async Task<bool> UpdateTransactionStatusAsync(int transactionId, string newStatus)
    {
        newStatus = newStatus.ToUpper();
        if (newStatus != "SUCCESS" && newStatus != "CANCELLED")
            return false;

        using var dbTransaction = await _dbContext.Database.BeginTransactionAsync();
        try
        {
            var tx = await _dbContext.Transactions.FindAsync(transactionId);
            if (tx == null)
                return false;

            if (tx.Status == "SUCCESS" || tx.Status == "CANCELLED")
                return false; // Already finalized

            tx.Status = newStatus;
            tx.CompletedAt = _clock.Now;

            if (newStatus == "SUCCESS" && tx.Type == "DEPOSIT")
            {
                var user = await _dbContext.Users.FindAsync(tx.UserId);
                if (user != null)
                {
                    // Add amount to user balance. Convert amount to int (financial token unit).
                    user.Balance = (user.Balance ?? 0) + (int)tx.Amount;
                }
            }

            _dbContext.ModerationNotices.Add(new ModerationNotice
            {
                UserId = tx.UserId,
                TransactionId = tx.TransactionId,
                Type = "TRANSACTION_RESOLVED",
                Title = newStatus == "SUCCESS" ? "Giao dịch đã hoàn thành" : "Giao dịch đã bị hủy",
                Message = $"Mã giao dịch: #{tx.TransactionId}\nSố tiền: {tx.Amount:N0}đ\nTrạng thái: {newStatus}\nNgày hoàn thành: {tx.CompletedAt:dd/MM/yyyy HH:mm}",
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

            // Create SUCCESS WITHDRAW transaction
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

            // Deduct balance and update tier
            user.Balance = (user.Balance ?? 0) - (int)premiumCost;
            user.TierId = 3; // Premium
            user.ExpiresAt = _clock.Now.AddDays(30);
            user.ExpiryNotified = false;

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
            CompletedAt = t.CompletedAt
        };
    }
}
