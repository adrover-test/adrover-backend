namespace Adrover.Application.DTOs.Payment;

public class CreateOrderRequest
{
    public Guid AdvertisementId { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class VerifyPaymentRequest
{
    public string OrderId { get; set; } = string.Empty;
    public string PaymentId { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
}

public class PaymentDto
{
    public Guid Id { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string? PaymentId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string Status { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string? CardLastFour { get; set; }
    public string? CardIssuer { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ProcessedAt { get; set; }
    public Guid? AdvertisementId { get; set; }
    public string? AdvertisementName { get; set; }
}

public class PaymentOrderDto
{
    public string OrderId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class PaymentHistoryResponse
{
    public List<PaymentDto> Items { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}
