using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Tasks;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudyHub.IntegrationTests;

public class AdminTransactionIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public AdminTransactionIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(User Admin, User Student, string AdminToken, string StudentToken)> SeedUsersAsync()
    {
        await _factory.SeedDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var admin = new User
        {
            Username = "admin.it",
            Email = $"admin_{Guid.NewGuid():N}@test.local",
            PasswordHash = "hashed",
            Role = "ADMIN",
            Status = "ACTIVE",
            TierId = 3,
            Balance = 1000000
        };
        var student = new User
        {
            Username = "student.it",
            Email = $"student_{Guid.NewGuid():N}@test.local",
            PasswordHash = "hashed",
            Role = "STUDENT",
            Status = "ACTIVE",
            TierId = 2,
            Balance = 100000
        };

        db.Users.AddRange(admin, student);
        await db.SaveChangesAsync();

        var adminToken = _factory.GenerateJwtToken(admin.UserId, admin.Username, admin.Role);
        var studentToken = _factory.GenerateJwtToken(student.UserId, student.Username, student.Role);

        return (admin, student, adminToken, studentToken);
    }

    [Fact]
    public async Task ReverseDeposit_ValidRequest_Returns200_DeductsBalance()
    {
        var (admin, student, adminToken, _) = await SeedUsersAsync();

        int depositTxId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
            var depositTx = new Transaction
            {
                UserId = student.UserId,
                Amount = 50000m,
                Type = "DEPOSIT",
                Status = "SUCCESS",
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
            db.Transactions.Add(depositTx);
            await db.SaveChangesAsync();
            depositTxId = depositTx.TransactionId;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/transactions/{depositTxId}/reverse-deposit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        request.Content = JsonContent.Create(new ReverseDepositDto { Reason = "Nhầm lẫn giao dịch nạp" });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
            var updatedStudent = await db.Users.FindAsync(student.UserId);
            Assert.Equal(50000, updatedStudent?.Balance); // 100_000 - 50_000

            var revTx = await db.Transactions.FirstOrDefaultAsync(t => t.OriginalTransactionId == depositTxId && t.Type == "REVERSE_DEPOSIT");
            Assert.NotNull(revTx);
            Assert.Equal(-50000m, revTx.Amount);
            Assert.Equal("SUCCESS", revTx.Status);

            var ledgerEntry = await db.BalanceLedgers.FirstOrDefaultAsync(l => l.TransactionId == revTx.TransactionId);
            Assert.NotNull(ledgerEntry);
            Assert.Equal("REVERSE_DEPOSIT", ledgerEntry.ActionType);
        }
    }

    [Fact]
    public async Task ReverseDeposit_InsufficientBalance_Returns422UnprocessableEntity()
    {
        var (admin, student, adminToken, _) = await SeedUsersAsync();

        int depositTxId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
            var studentUser = await db.Users.FindAsync(student.UserId);
            studentUser!.Balance = 10000; // Less than 50_000

            var depositTx = new Transaction
            {
                UserId = student.UserId,
                Amount = 50000m,
                Type = "DEPOSIT",
                Status = "SUCCESS",
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
            db.Transactions.Add(depositTx);
            await db.SaveChangesAsync();
            depositTxId = depositTx.TransactionId;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/transactions/{depositTxId}/reverse-deposit");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        request.Content = JsonContent.Create(new ReverseDepositDto { Reason = "Thu hồi nhưng không đủ số dư" });

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task ReverseDeposit_DuplicateRequest_Returns409Conflict()
    {
        var (admin, student, adminToken, _) = await SeedUsersAsync();

        int depositTxId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
            var depositTx = new Transaction
            {
                UserId = student.UserId,
                Amount = 20000m,
                Type = "DEPOSIT",
                Status = "SUCCESS",
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
            db.Transactions.Add(depositTx);
            await db.SaveChangesAsync();
            depositTxId = depositTx.TransactionId;
        }

        // 1st reversal
        var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/transactions/{depositTxId}/reverse-deposit");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        req1.Content = JsonContent.Create(new ReverseDepositDto { Reason = "Lần 1" });
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2nd reversal (duplicate)
        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/transactions/{depositTxId}/reverse-deposit");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        req2.Content = JsonContent.Create(new ReverseDepositDto { Reason = "Lần 2" });
        var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.Conflict, res2.StatusCode);
    }

    [Fact]
    public async Task RefundTransaction_ForDeposit_Returns422UnprocessableEntity()
    {
        var (admin, student, adminToken, _) = await SeedUsersAsync();

        int depositTxId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
            var depositTx = new Transaction
            {
                UserId = student.UserId,
                Amount = 20000m,
                Type = "DEPOSIT",
                Status = "SUCCESS",
                StartedAt = DateTime.UtcNow,
                CompletedAt = DateTime.UtcNow
            };
            db.Transactions.Add(depositTx);
            await db.SaveChangesAsync();
            depositTxId = depositTx.TransactionId;
        }

        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/admin/transactions/{depositTxId}/refund");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        req.Content = JsonContent.Create(new RefundRequestDto { Reason = "Cố tình refund deposit sai API" });

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, res.StatusCode);
    }
}
