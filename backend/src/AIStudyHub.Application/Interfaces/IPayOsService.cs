using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace AIStudyHub.Application.Interfaces;

public class CreatePaymentLinkRequestDto
{
    public long OrderCode { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ReturnUrl { get; set; } = string.Empty;
    public string CancelUrl { get; set; } = string.Empty;
    public string? BuyerName { get; set; }
    public string? BuyerEmail { get; set; }
    public string? BuyerPhone { get; set; }
}

public class CreatePaymentLinkResponseDto
{
    public string Bin { get; set; } = string.Empty;
    public string AccountNumber { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public long OrderCode { get; set; }
    public string Currency { get; set; } = "VND";
    public string PaymentLinkId { get; set; } = string.Empty;
    public string Status { get; set; } = "PENDING";
    public string CheckoutUrl { get; set; } = string.Empty;
    public string QrCode { get; set; } = string.Empty;
}

public class PayOsPaymentInfoResponseDto
{
    public string? Id { get; set; }
    public long OrderCode { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountPaid { get; set; }
    public decimal AmountRemaining { get; set; }
    public string Status { get; set; } = string.Empty; // PENDING, PAID, CANCELLED, EXPIRED
    public string CreatedAt { get; set; } = string.Empty;
    public string? CanceledAt { get; set; }
    public string? CancellationReason { get; set; }
    public string? CheckoutUrl { get; set; }
}

public class PayOsWebhookDataDto
{
    public long OrderCode { get; set; }
    public decimal Amount { get; set; }
    public string? Description { get; set; }
    public string? AccountNumber { get; set; }
    public string? Reference { get; set; }
    public string? TransactionDateTime { get; set; }
    public string Currency { get; set; } = "VND";
    public string? PaymentLinkId { get; set; }
    public string? Code { get; set; }
    public string? Desc { get; set; }
    public string? CounterAccountBankId { get; set; }
    public string? CounterAccountBankName { get; set; }
    public string? CounterAccountName { get; set; }
    public string? CounterAccountNumber { get; set; }
    public string? VirtualAccountName { get; set; }
    public string? VirtualAccountNumber { get; set; }
}

public class PayOsWebhookPayloadDto
{
    public string? Code { get; set; }
    public string? Desc { get; set; }
    public PayOsWebhookDataDto? Data { get; set; }
    public string? Signature { get; set; }
}

public interface IPayOsService
{
    string CreatePaymentRequestSignature(long amount, string cancelUrl, string description, long orderCode, string returnUrl, string checksumKey);
    bool VerifyWebhookSignature(PayOsWebhookDataDto data, string signature, string checksumKey);
    Task<CreatePaymentLinkResponseDto> CreatePaymentLinkAsync(CreatePaymentLinkRequestDto request, CancellationToken cancellationToken = default);
    Task<PayOsPaymentInfoResponseDto?> GetPaymentRequestAsync(long orderCode, CancellationToken cancellationToken = default);
}
