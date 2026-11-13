using Adrover.Application.DTOs.Payment;
using Adrover.Application.Exceptions;
using Adrover.Domain.Entities;
using Adrover.Data.Repositories;
using Adrover.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Adrover.Application.Services;

/// <summary>
/// Service for managing payments and orders
/// </summary>
public class PaymentService : IPaymentService
{
    private readonly IRepository<Payment> _paymentRepository;
    private readonly IRepository<Advertisement> _advertisementRepository;
    private readonly IRepository<User> _userRepository;
    private readonly IRepository<Wallet> _walletRepository;
    private readonly IPaymentGatewayService _paymentGateway;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(
        IRepository<Payment> paymentRepository,
        IRepository<Advertisement> advertisementRepository,
        IRepository<User> userRepository,
        IRepository<Wallet> walletRepository,
        IPaymentGatewayService paymentGateway,
        ILogger<PaymentService> logger)
    {
        _paymentRepository = paymentRepository;
        _advertisementRepository = advertisementRepository;
        _userRepository = userRepository;
        _walletRepository = walletRepository;
        _paymentGateway = paymentGateway;
        _logger = logger;
    }

    public async Task<PaymentOrderDto> CreateOrderAsync(Guid userId, CreateOrderRequest request)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        var advertisement = await _advertisementRepository.GetByIdAsync(request.AdvertisementId);
        if (advertisement == null)
            throw new NotFoundException("Advertisement not found");

        if (advertisement.ClientId != userId)
            throw new AuthorizationException("You can only pay for your own advertisements");

        if (request.Amount <= 0)
            throw new Application.Exceptions.ValidationException("Amount must be greater than 0");

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            AdvertisementId = request.AdvertisementId,
            OrderId = GenerateOrderId(),
            Amount = request.Amount,
            Currency = "INR",
            Status = PaymentStatus.Pending,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                { "email", user.Email },
                { "phone", user.Phone },
                { "name", user.FullName },
                { "advertisementId", request.AdvertisementId.ToString() }
            }
        };

        await _paymentRepository.AddAsync(payment);

        _logger.LogInformation("Order created: {OrderId} for user {UserId}", payment.OrderId, userId);

        return new PaymentOrderDto
        {
            OrderId = payment.OrderId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString(),
            CreatedAt = payment.CreatedAt
        };
    }

    public async Task<PaymentDto> VerifyPaymentAsync(Guid userId, VerifyPaymentRequest request)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.OrderId == request.OrderId);
        if (payment == null)
            throw new NotFoundException("Payment order not found");

        if (payment.UserId != userId)
            throw new AuthorizationException("You can only verify your own payments");

        if (payment.Status != PaymentStatus.Pending)
            throw new Application.Exceptions.ValidationException("This payment has already been processed");

        try
        {
            var isValid = _paymentGateway.VerifyPaymentSignature(
                payment.OrderId,
                request.PaymentId,
                request.Signature);

            if (!isValid)
            {
                payment.Status = PaymentStatus.Failed;
                payment.FailedAt = DateTime.UtcNow;
                await _paymentRepository.UpdateAsync(payment);
                throw new Application.Exceptions.ValidationException("Payment signature verification failed");
            }

            payment.Status = PaymentStatus.Completed;
            payment.PaymentId = request.PaymentId;
            payment.Signature = request.Signature;
            payment.ProcessedAt = DateTime.UtcNow;

            await _paymentRepository.UpdateAsync(payment);

            // Update advertisement as paid
            if (payment.AdvertisementId.HasValue)
            {
                var advertisement = await _advertisementRepository.GetByIdAsync(payment.AdvertisementId.Value);
                if (advertisement != null)
                {
                    advertisement.PaymentId = payment.Id;
                    advertisement.PaidAmount = payment.Amount;
                    await _advertisementRepository.UpdateAsync(advertisement);
                }
            }

            _logger.LogInformation("Payment verified: {OrderId} with paymentId {PaymentId}", 
                payment.OrderId, request.PaymentId);

            return MapToPaymentDto(payment);
        }
        catch (Application.Exceptions.ValidationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying payment: {OrderId}", request.OrderId);
            throw new Application.Exceptions.ValidationException("Failed to verify payment");
        }
    }

    public async Task<PaymentDto> GetPaymentAsync(Guid id)
    {
        var payment = await _paymentRepository.GetByIdAsync(id);
        if (payment == null)
            throw new NotFoundException("Payment not found");

        return MapToPaymentDto(payment);
    }

    public async Task<PaymentHistoryResponse> GetPaymentHistoryAsync(Guid userId, int pageNumber = 1, int pageSize = 10)
    {
        if (pageNumber < 1 || pageSize < 1 || pageSize > 100)
            throw new Application.Exceptions.ValidationException("Invalid pagination parameters");

        var payments = await _paymentRepository.FindAsync(p => p.UserId == userId);
        var orderedPayments = payments.OrderByDescending(p => p.CreatedAt).ToList();

        var total = orderedPayments.Count;
        var skip = (pageNumber - 1) * pageSize;
        var pagedItems = orderedPayments.Skip(skip).Take(pageSize).ToList();

        var items = pagedItems.Select(MapToPaymentDto).ToList();

        return new PaymentHistoryResponse
        {
            Total = total,
            Skip = skip,
            Take = pageSize,
            Items = items
        };
    }

    public async Task<PaymentDto> ProcessWebhookAsync(string jsonPayload, string signature)
    {
        throw new NotImplementedException("Webhook processing requires Razorpay webhook configuration");
    }

    private static string GenerateOrderId()
    {
        return $"ORDER_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";
    }

    private static PaymentDto MapToPaymentDto(Payment payment)
    {
        return new PaymentDto
        {
            Id = payment.Id,
            OrderId = payment.OrderId,
            PaymentId = payment.PaymentId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString(),
            PaymentMethod = payment.PaymentMethod,
            CardLastFour = payment.CardLastFour,
            CardIssuer = payment.CardIssuer,
            CreatedAt = payment.CreatedAt,
            ProcessedAt = payment.ProcessedAt,
            AdvertisementId = payment.AdvertisementId
        };
    }
}
