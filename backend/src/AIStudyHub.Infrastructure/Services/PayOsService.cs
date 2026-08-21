using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Infrastructure.Services;

public class PayOsService : IPayOsService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PayOsService> _logger;

    public PayOsService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<PayOsService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    private string GetClientId() => _configuration["PayOS:ClientId"] ?? string.Empty;
    private string GetApiKey() => _configuration["PayOS:ApiKey"] ?? string.Empty;
    private string GetChecksumKey() => _configuration["PayOS:ChecksumKey"] ?? string.Empty;

    public string CreatePaymentRequestSignature(long amount, string cancelUrl, string description, long orderCode, string returnUrl, string checksumKey)
    {
        // 1. PayOS create-payment-request signature canonical format (5 fields sorted alphabetically):
        // amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}
        var canonical = $"amount={amount}&cancelUrl={cancelUrl}&description={description}&orderCode={orderCode}&returnUrl={returnUrl}";
        return ComputeHmacSha256(canonical, checksumKey);
    }

    public bool VerifyWebhookSignature(PayOsWebhookDataDto data, string signature, string checksumKey)
    {
        if (data == null || string.IsNullOrWhiteSpace(signature))
            return false;

        var canonical = CanonicalizeWebhookData(data);
        var expectedSignature = ComputeHmacSha256(canonical, checksumKey);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedSignature.ToLowerInvariant()),
            Encoding.UTF8.GetBytes(signature.Trim().ToLowerInvariant()));
    }

    public static string CanonicalizeWebhookData(PayOsWebhookDataDto data)
    {
        // PayOS official webhook canonicalization:
        // Extract fields, sort keys alphabetically, normalize null/"null"/"undefined" to empty string ("key="), join with "&"
        var dict = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["accountNumber"] = NormalizeValue(data.AccountNumber),
            ["amount"] = ((long)data.Amount).ToString(),
            ["code"] = NormalizeValue(data.Code),
            ["counterAccountBankId"] = NormalizeValue(data.CounterAccountBankId),
            ["counterAccountBankName"] = NormalizeValue(data.CounterAccountBankName),
            ["counterAccountName"] = NormalizeValue(data.CounterAccountName),
            ["counterAccountNumber"] = NormalizeValue(data.CounterAccountNumber),
            ["currency"] = NormalizeValue(data.Currency),
            ["desc"] = NormalizeValue(data.Desc),
            ["description"] = NormalizeValue(data.Description),
            ["orderCode"] = data.OrderCode.ToString(),
            ["paymentLinkId"] = NormalizeValue(data.PaymentLinkId),
            ["reference"] = NormalizeValue(data.Reference),
            ["transactionDateTime"] = NormalizeValue(data.TransactionDateTime),
            ["virtualAccountName"] = NormalizeValue(data.VirtualAccountName),
            ["virtualAccountNumber"] = NormalizeValue(data.VirtualAccountNumber)
        };

        var sb = new StringBuilder();
        bool first = true;
        foreach (var kv in dict)
        {
            if (!first) sb.Append('&');
            sb.Append(kv.Key).Append('=').Append(kv.Value);
            first = false;
        }

        return sb.ToString();
    }

    private static string NormalizeValue(string? val)
    {
        if (val == null || val == "null" || val == "undefined")
            return string.Empty;
        return val;
    }

    public static string ComputeHmacSha256(string data, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public async Task<CreatePaymentLinkResponseDto> CreatePaymentLinkAsync(CreatePaymentLinkRequestDto request, CancellationToken cancellationToken = default)
    {
        var clientId = GetClientId();
        var apiKey = GetApiKey();
        var checksumKey = GetChecksumKey();
        bool useMock = _configuration.GetValue<bool>("PayOS:UseMock") 
                    || _configuration.GetValue<bool>("Testing:UseInMemoryDb");

        // 1. Explicit mock mode for local testing
        if (useMock)
        {
            _logger.LogInformation("PayOS running in Mock mode. Generating mock payment link for order {OrderCode}", request.OrderCode);
            var mockLinkId = $"mock_link_{request.OrderCode}";
            return new CreatePaymentLinkResponseDto
            {
                Bin = "970422",
                AccountNumber = "0123456789",
                AccountName = "AI STUDY HUB MOCK",
                Amount = request.Amount,
                Description = request.Description,
                OrderCode = request.OrderCode,
                Currency = "VND",
                PaymentLinkId = mockLinkId,
                Status = "PENDING",
                CheckoutUrl = $"https://pay.payos.vn/web/{mockLinkId}",
                QrCode = $"vietqr://mock?amount={request.Amount}&orderCode={request.OrderCode}"
            };
        }

        // 2. Fail-closed: Missing credentials throw immediately
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(checksumKey))
        {
            throw new InvalidOperationException("PayOS credentials (ClientId, ApiKey, ChecksumKey) must be configured when PayOS:UseMock is false.");
        }

        var signature = CreatePaymentRequestSignature(
            (long)request.Amount,
            request.CancelUrl,
            request.Description,
            request.OrderCode,
            request.ReturnUrl,
            checksumKey);

        var payload = new
        {
            orderCode = request.OrderCode,
            amount = (long)request.Amount,
            description = request.Description,
            buyerName = request.BuyerName,
            buyerEmail = request.BuyerEmail,
            buyerPhone = request.BuyerPhone,
            returnUrl = request.ReturnUrl,
            cancelUrl = request.CancelUrl,
            signature = signature
        };

        var response = await _httpClient.PostAsJsonAsync("https://api-merchant.payos.vn/v2/payment-requests", payload, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("PayOS CreatePaymentLink failed with status {StatusCode}: {ErrorBody}", response.StatusCode, errorBody);
            throw new HttpRequestException($"PayOS API failed with status {response.StatusCode}: {errorBody}");
        }

        var json = await response.Content.ReadFromJsonAsync<PayOsApiResponse<CreatePaymentLinkResponseDto>>(cancellationToken: cancellationToken);
        if (json?.Data == null)
        {
            throw new InvalidOperationException("PayOS returned empty response payload.");
        }

        return json.Data;
    }

    public async Task<PayOsPaymentInfoResponseDto?> GetPaymentRequestAsync(long orderCode, CancellationToken cancellationToken = default)
    {
        var clientId = GetClientId();
        var apiKey = GetApiKey();
        bool useMock = _configuration.GetValue<bool>("PayOS:UseMock") 
                    || _configuration.GetValue<bool>("Testing:UseInMemoryDb");

        if (useMock)
        {
            return new PayOsPaymentInfoResponseDto
            {
                OrderCode = orderCode,
                Status = "PENDING",
                Amount = 0,
                AmountPaid = 0
            };
        }

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("PayOS credentials (ClientId, ApiKey) must be configured when PayOS:UseMock is false.");
        }

        using var httpRequest = new HttpRequestMessage(HttpMethod.Get, $"https://api-merchant.payos.vn/v2/payment-requests/{orderCode}");
        httpRequest.Headers.Add("x-client-id", clientId);
        httpRequest.Headers.Add("x-api-key", apiKey);

        var response = await _httpClient.SendAsync(httpRequest, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("PayOS query payment request {OrderCode} returned HTTP {StatusCode}", orderCode, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadFromJsonAsync<PayOsApiResponse<PayOsPaymentInfoResponseDto>>(cancellationToken: cancellationToken);
        return json?.Data;
    }

    private class PayOsApiResponse<T>
    {
        public string Code { get; set; } = string.Empty;
        public string Desc { get; set; } = string.Empty;
        public T? Data { get; set; }
        public string? Signature { get; set; }
    }
}
