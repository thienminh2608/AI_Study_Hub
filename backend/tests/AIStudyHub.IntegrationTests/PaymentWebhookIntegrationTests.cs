using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIStudyHub.Api.Controllers;
using AIStudyHub.Application.DTOs;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Domain.Entities;
using AIStudyHub.Infrastructure.Persistence;
using AIStudyHub.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AIStudyHub.IntegrationTests;

public class PaymentWebhookIntegrationTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string ChecksumKey = "d87a9b76e2c34f19b28a9c3d4e5f6071";

    public PaymentWebhookIntegrationTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<User> CreateUserAndPendingTxAsync(long orderCode, decimal amount)
    {
        await _factory.SeedDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var user = new User
        {
            Username = $"payer_{orderCode}",
            Email = $"payer_{orderCode}@test.com",
            PasswordHash = "hash",
            Role = "STUDENT",
            Status = "ACTIVE",
            TierId = 1,
            Balance = 5000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var tx = new Transaction
        {
            UserId = user.UserId,
            Amount = amount,
            Type = "DEPOSIT",
            Status = "PENDING",
            PayOsOrderCode = orderCode,
            StartedAt = DateTime.UtcNow,
            RequiresManualReview = false
        };
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();

        return user;
    }

    [Fact]
    public async Task Webhook_With_Valid_Signature_Processes_Deposit_Atomically()
    {
        long orderCode = 777001;
        decimal amount = 50000;
        var user = await CreateUserAndPendingTxAsync(orderCode, amount);

        var webhookData = new PayOsWebhookDataDto
        {
            OrderCode = orderCode,
            Amount = amount,
            Description = "Nap tien hoc phi",
            AccountNumber = "0123456789",
            Reference = $"REF_{orderCode}",
            TransactionDateTime = "2026-08-20 12:00:00",
            Currency = "VND",
            PaymentLinkId = "link_777001",
            Code = "00",
            Desc = "success"
        };

        var canonical = PayOsService.CanonicalizeWebhookData(webhookData);
        var signature = PayOsService.ComputeHmacSha256(canonical, ChecksumKey);

        var payload = new
        {
            code = "00",
            desc = "success",
            data = webhookData,
            signature = signature
        };

        var response = await _client.PostAsJsonAsync("/api/payment/payos/webhook", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify user balance is credited: 5000 + 50000 = 55000
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var updatedUser = await db.Users.FindAsync(user.UserId);
        Assert.Equal(55000, updatedUser!.Balance);

        var updatedTx = await db.Transactions.FirstOrDefaultAsync(t => t.PayOsOrderCode == orderCode);
        Assert.Equal("SUCCESS", updatedTx!.Status);

        var eventRecord = await db.PaymentWebhookEvents.FirstOrDefaultAsync(e => e.MerchantOrderCode == orderCode);
        Assert.NotNull(eventRecord);
        Assert.Equal("PROCESSED", eventRecord.Status);
    }

    [Fact]
    public async Task Duplicate_Webhook_Returns_200_Without_Double_Crediting()
    {
        long orderCode = 777002;
        decimal amount = 100000;
        var user = await CreateUserAndPendingTxAsync(orderCode, amount);

        var webhookData = new PayOsWebhookDataDto
        {
            OrderCode = orderCode,
            Amount = amount,
            Description = "Nap tien 100k",
            AccountNumber = "0123456789",
            Reference = $"REF_{orderCode}",
            TransactionDateTime = "2026-08-20 12:00:00",
            Currency = "VND",
            PaymentLinkId = "link_777002",
            Code = "00",
            Desc = "success"
        };

        var canonical = PayOsService.CanonicalizeWebhookData(webhookData);
        var signature = PayOsService.ComputeHmacSha256(canonical, ChecksumKey);

        var payload = new
        {
            code = "00",
            desc = "success",
            data = webhookData,
            signature = signature
        };

        // 1st request
        var res1 = await _client.PostAsJsonAsync("/api/payment/payos/webhook", payload);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 2nd request (duplicate replay)
        var res2 = await _client.PostAsJsonAsync("/api/payment/payos/webhook", payload);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        // Verify user balance is ONLY credited ONCE (5000 + 100000 = 105000)
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var updatedUser = await db.Users.FindAsync(user.UserId);
        Assert.Equal(105000, updatedUser!.Balance);
    }

    [Fact]
    public async Task Webhook_With_Amount_Mismatch_Records_Reconciliation_Case_And_Flags_Review()
    {
        long orderCode = 777003;
        decimal expectedAmount = 200000;
        var user = await CreateUserAndPendingTxAsync(orderCode, expectedAmount);

        // Webhook arrives with 50,000 instead of 200,000
        var webhookData = new PayOsWebhookDataDto
        {
            OrderCode = orderCode,
            Amount = 50000,
            Description = "Nap tien 50k",
            AccountNumber = "0123456789",
            Reference = $"REF_{orderCode}",
            TransactionDateTime = "2026-08-20 12:00:00",
            Currency = "VND",
            PaymentLinkId = "link_777003",
            Code = "00",
            Desc = "success"
        };

        var canonical = PayOsService.CanonicalizeWebhookData(webhookData);
        var signature = PayOsService.ComputeHmacSha256(canonical, ChecksumKey);

        var payload = new
        {
            code = "00",
            desc = "success",
            data = webhookData,
            signature = signature
        };

        var response = await _client.PostAsJsonAsync("/api/payment/payos/webhook", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify balance was NOT touched
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var updatedUser = await db.Users.FindAsync(user.UserId);
        Assert.Equal(5000, updatedUser!.Balance);

        // Verify reconciliation case exists
        var reconCase = await db.PaymentReconciliationCases.FirstOrDefaultAsync(c => c.PayOsOrderCode == orderCode);
        Assert.NotNull(reconCase);
        Assert.Equal("AMOUNT_MISMATCH", reconCase.IssueType);

        var tx = await db.Transactions.FirstOrDefaultAsync(t => t.PayOsOrderCode == orderCode);
        Assert.True(tx!.RequiresManualReview);
    }

    [Fact]
    public async Task Create_Payment_Link_Endpoint_Creates_Pending_Transaction()
    {
        await _factory.SeedDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var user = new User
        {
            Username = "linkcreator",
            Email = "linkcreator@test.com",
            PasswordHash = "hash",
            Role = "STUDENT",
            Status = "ACTIVE",
            TierId = 1,
            Balance = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var token = _factory.GenerateJwtToken(user.UserId, user.Username, user.Role);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var requestBody = new { amount = 50000 };
        var response = await _client.PostAsJsonAsync("/api/transaction/payos/create-link", requestBody);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("checkoutUrl", out var checkoutUrl));
        Assert.False(string.IsNullOrWhiteSpace(checkoutUrl.GetString()));

        Assert.True(json.TryGetProperty("orderCode", out var orderCodeProp));
        long orderCode = orderCodeProp.GetInt64();

        // Check transaction in DB
        using var scope2 = _factory.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<StudyHubDbContext>();
        var tx = await db2.Transactions.FirstOrDefaultAsync(t => t.PayOsOrderCode == orderCode);
        Assert.NotNull(tx);
        Assert.Equal("PENDING", tx.Status);
        Assert.Equal(50000, tx.Amount);
    }

    [Fact]
    public void Payload_Sanitizer_Masks_Account_Numbers_And_Names()
    {
        var payload = new PayOsWebhookPayloadDto
        {
            Code = "00",
            Desc = "success",
            Data = new PayOsWebhookDataDto
            {
                OrderCode = 123456,
                Amount = 100000,
                AccountNumber = "098765432109",
                CounterAccountNumber = "112233445566",
                CounterAccountName = "NGUYEN VAN A",
                VirtualAccountNumber = "9988776655",
                VirtualAccountName = "STUDENT NGUYEN"
            }
        };

        var sanitized = PaymentWebhookController.SanitizeWebhookPayload(payload);

        Assert.Contains("***2109", sanitized);
        Assert.Contains("***5566", sanitized);
        Assert.Contains("***6655", sanitized);
        Assert.DoesNotContain("098765432109", sanitized);
        Assert.DoesNotContain("112233445566", sanitized);
        Assert.DoesNotContain("9988776655", sanitized);
    }

    [Fact]
    public async Task Concurrent_Webhooks_For_Same_Order_Only_One_Claims_And_Credits()
    {
        await _factory.SeedDatabaseAsync();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StudyHubDbContext>();

        var user = new User
        {
            Username = "concurrent_webhook_user",
            Email = "concurrent_webhook@test.com",
            PasswordHash = "hash",
            Role = "STUDENT",
            Status = "ACTIVE",
            TierId = 1,
            Balance = 10000,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();

        long orderCode = 777123456;
        var tx = new Transaction
        {
            UserId = user.UserId,
            Amount = 100000,
            Type = "DEPOSIT",
            Status = "PENDING",
            PayOsOrderCode = orderCode,
            StartedAt = DateTime.UtcNow,
            RequiresManualReview = false
        };
        db.Transactions.Add(tx);
        await db.SaveChangesAsync();

        var webhookData = new PayOsWebhookDataDto
        {
            OrderCode = orderCode,
            Amount = 100000,
            Description = "Nap vi",
            AccountNumber = "0123456789",
            Reference = "REF_CONCURRENT_1",
            TransactionDateTime = "2026-08-20 12:00:00",
            Currency = "VND",
            PaymentLinkId = "link_concurrent",
            Code = "00",
            Desc = "success"
        };

        var canonical = PayOsService.CanonicalizeWebhookData(webhookData);
        var signature = PayOsService.ComputeHmacSha256(canonical, ChecksumKey);

        // Fire 2 concurrent webhook requests
        var payload1 = new { code = "00", desc = "success", data = webhookData, signature = signature };
        var payload2 = new { code = "00", desc = "success", data = webhookData, signature = signature };

        var task1 = _client.PostAsJsonAsync("/api/payment/payos/webhook", payload1);
        var task2 = _client.PostAsJsonAsync("/api/payment/payos/webhook", payload2);

        var responses = await Task.WhenAll(task1, task2);

        Assert.Equal(HttpStatusCode.OK, responses[0].StatusCode);
        Assert.Equal(HttpStatusCode.OK, responses[1].StatusCode);

        // Verify balance was credited EXACTLY ONCE (10,000 + 100,000 = 110,000)
        using var verifyScope = _factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<StudyHubDbContext>();
        var verifyUser = await verifyDb.Users.FindAsync(user.UserId);
        Assert.Equal(110000, verifyUser!.Balance);
    }
}
