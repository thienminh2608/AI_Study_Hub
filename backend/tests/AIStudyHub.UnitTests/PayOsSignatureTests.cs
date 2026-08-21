using System;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using AIStudyHub.Application.Interfaces;
using AIStudyHub.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AIStudyHub.UnitTests;

public class PayOsSignatureTests
{
    private readonly PayOsService _payOsService;
    private const string TestChecksumKey = "d87a9b76e2c34f19b28a9c3d4e5f6071";

    public PayOsSignatureTests()
    {
        var config = new ConfigurationBuilder().Build();
        _payOsService = new PayOsService(new HttpClient(), config, NullLogger<PayOsService>.Instance);
    }

    [Fact]
    public void CreatePaymentRequestSignature_Formats_5_Canonical_Fields_Alphabetically()
    {
        long amount = 50000;
        string cancelUrl = "https://example.com/cancel";
        string description = "Nap vi 123456";
        long orderCode = 123456;
        string returnUrl = "https://example.com/return";

        var signature = _payOsService.CreatePaymentRequestSignature(
            amount, cancelUrl, description, orderCode, returnUrl, TestChecksumKey);

        Assert.NotNull(signature);
        Assert.Equal(64, signature.Length); // 256 bits = 64 hex characters

        // Verify manual canonical matches
        var expectedCanonical = $"amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}";
        var expectedSignature = PayOsService.ComputeHmacSha256(expectedCanonical, TestChecksumKey);

        Assert.Equal(expectedSignature, signature);
    }

    [Fact]
    public void VerifyWebhookSignature_Correctly_Validates_Official_PayOs_Canonical_String()
    {
        var data = new PayOsWebhookDataDto
        {
            OrderCode = 123456,
            Amount = 100000,
            Description = "Nap tien hoc phi",
            AccountNumber = "0123456789",
            Reference = "REF99999",
            TransactionDateTime = "2026-08-20 12:00:00",
            Currency = "VND",
            PaymentLinkId = "link_abc123",
            Code = "00",
            Desc = "success",
            CounterAccountBankId = null, // null converted to empty key=
            CounterAccountBankName = null,
            CounterAccountName = null,
            CounterAccountNumber = null,
            VirtualAccountName = null,
            VirtualAccountNumber = null
        };

        var canonical = PayOsService.CanonicalizeWebhookData(data);
        var expectedSignature = PayOsService.ComputeHmacSha256(canonical, TestChecksumKey);

        // Verification must pass with correct signature
        bool isValid = _payOsService.VerifyWebhookSignature(data, expectedSignature, TestChecksumKey);
        Assert.True(isValid);

        // Verification must fail when amount is tampered
        data.Amount = 99999;
        bool isTamperedValid = _payOsService.VerifyWebhookSignature(data, expectedSignature, TestChecksumKey);
        Assert.False(isTamperedValid);
    }
}
