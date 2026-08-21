using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using AIStudyHub.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AIStudyHub.Api.Controllers;

[ApiController]
[Route("api/payment")]
public class PaymentWebhookController : ControllerBase
{
    private readonly IPayOsService _payOsService;
    private readonly IPaymentCompletionService _paymentCompletionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(
        IPayOsService payOsService,
        IPaymentCompletionService paymentCompletionService,
        IConfiguration configuration,
        ILogger<PaymentWebhookController> logger)
    {
        _payOsService = payOsService;
        _paymentCompletionService = paymentCompletionService;
        _configuration = configuration;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpPost("payos/webhook")]
    [RequestSizeLimit(65536)] // 64 KB limit
    public async Task<IActionResult> HandlePayOsWebhook()
    {
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(rawBody))
        {
            return BadRequest(new { message = "Empty webhook payload" });
        }

        PayOsWebhookPayloadDto? payload;
        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            payload = JsonSerializer.Deserialize<PayOsWebhookPayloadDto>(rawBody, options);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize PayOS webhook payload");
            return BadRequest(new { message = "Invalid JSON format" });
        }

        if (payload?.Data == null || string.IsNullOrWhiteSpace(payload.Signature))
        {
            return BadRequest(new { message = "Missing PayOS webhook data or signature" });
        }

        var checksumKey = _configuration["PayOS:ChecksumKey"];
        if (string.IsNullOrWhiteSpace(checksumKey))
        {
            _logger.LogError("PayOS ChecksumKey is not configured in this environment.");
            return StatusCode(500, new { message = "PayOS ChecksumKey is not configured." });
        }

        var isSignatureValid = _payOsService.VerifyWebhookSignature(payload.Data, payload.Signature, checksumKey);
        if (!isSignatureValid)
        {
            _logger.LogWarning("Invalid PayOS webhook signature for orderCode {OrderCode}", payload.Data.OrderCode);
            return Unauthorized(new { message = "Invalid PayOS signature" });
        }

        var rawBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawBody));
        var payloadHash = Convert.ToHexString(rawBytes).ToLowerInvariant();

        var sanitizedJson = SanitizeWebhookPayload(payload);

        // Process webhook through transactional atomic service
        var result = await _paymentCompletionService.ProcessWebhookTransactionallyAsync(
            payload,
            sanitizedJson,
            payloadHash,
            HttpContext.RequestAborted);

        return Ok(new
        {
            success = result.Success,
            isDuplicate = result.IsDuplicate,
            requiresManualReview = result.RequiresManualReview,
            message = result.Message
        });
    }

    public static string SanitizeWebhookPayload(PayOsWebhookPayloadDto payload)
    {
        if (payload?.Data == null) return "{}";

        var d = payload.Data;
        var sanitizedData = new
        {
            orderCode = d.OrderCode,
            amount = d.Amount,
            description = d.Description,
            accountNumber = MaskFinancialInfo(d.AccountNumber),
            reference = d.Reference,
            transactionDateTime = d.TransactionDateTime,
            currency = d.Currency,
            paymentLinkId = d.PaymentLinkId,
            code = d.Code,
            desc = d.Desc,
            counterAccountBankId = d.CounterAccountBankId,
            counterAccountBankName = d.CounterAccountBankName,
            counterAccountName = MaskName(d.CounterAccountName),
            counterAccountNumber = MaskFinancialInfo(d.CounterAccountNumber),
            virtualAccountName = MaskName(d.VirtualAccountName),
            virtualAccountNumber = MaskFinancialInfo(d.VirtualAccountNumber)
        };

        var sanitizedRoot = new
        {
            code = payload.Code,
            desc = payload.Desc,
            data = sanitizedData
        };

        return JsonSerializer.Serialize(sanitizedRoot);
    }

    private static string? MaskFinancialInfo(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return val;
        val = val.Trim();
        if (val.Length <= 4) return "****";
        return $"***{val[^4..]}";
    }

    private static string? MaskName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var parts = name.Trim().Split(' ');
        if (parts.Length == 1) return $"{parts[0][0]}***";
        return $"{parts[0]} *** {parts[^1]}";
    }
}
