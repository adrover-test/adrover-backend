using Adrover.Application.DTOs.Payment;

namespace Adrover.Application.Services;

/// <summary>
/// Interface for payment management service
/// </summary>
public interface IPaymentService
{
    Task<PaymentOrderDto> CreateOrderAsync(Guid userId, CreateOrderRequest request);
    Task<PaymentDto> VerifyPaymentAsync(Guid userId, VerifyPaymentRequest request);
    Task<PaymentDto> GetPaymentAsync(Guid id);
    Task<PaymentHistoryResponse> GetPaymentHistoryAsync(Guid userId, int pageNumber = 1, int pageSize = 10);
    Task<PaymentDto> ProcessWebhookAsync(string jsonPayload, string signature);
}
