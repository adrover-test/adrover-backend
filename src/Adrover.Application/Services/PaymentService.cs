using Adrover.Application.DTOs.Payment;
using Adrover.Application.Exceptions;
using Adrover.Domain.Entities;
using Adrover.Data.Repositories;
using Adrover.Infrastructure.Services;
using Microsoft.Extensions.Logging;
using System.Text.Json;

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

        if (advertisement.ApprovalAmount <= 0)
            throw new Application.Exceptions.ValidationException("The advertisement does not have a payable amount");

        if (request.Amount != advertisement.ApprovalAmount)
            throw new Application.Exceptions.ValidationException("Amount must match the advertisement's approved amount");

        var amountInPaise = advertisement.ApprovalAmount * 100m;
        if (amountInPaise != decimal.Truncate(amountInPaise) || amountInPaise > int.MaxValue)
            throw new Application.Exceptions.ValidationException("Amount must be a valid Razorpay amount in paise");

        var paymentId = Guid.NewGuid();
        var metadata = new Dictionary<string, string>
        {
            { "email", user.Email },
            { "phone", user.Phone },
            { "name", user.FullName },
            { "advertisementId", request.AdvertisementId.ToString() },
            { "paymentId", paymentId.ToString() }
        };
        var razorpayOrder = await _paymentGateway.CreateOrderAsync(
            advertisement.ApprovalAmount,
            "INR",
            $"PAY_{paymentId:N}",
            metadata);

        var payment = new Payment
        {
            Id = paymentId,
            UserId = userId,
            AdvertisementId = request.AdvertisementId,
            OrderId = razorpayOrder.Id,
            Amount = advertisement.ApprovalAmount,
            Currency = "INR",
            Status = PaymentStatus.Pending,
            Description = request.Description,
            CreatedAt = DateTime.UtcNow,
            Metadata = metadata
        };

        await _paymentRepository.AddAsync(payment);

        _logger.LogInformation("Razorpay order created: {OrderId} for user {UserId}", payment.OrderId, userId);

        return new PaymentOrderDto
        {
            OrderId = payment.OrderId,
            Amount = payment.Amount,
            Currency = payment.Currency,
            Status = payment.Status.ToString(),
            CreatedAt = payment.CreatedAt,
            KeyId = _paymentGateway.KeyId
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

        var gatewayPayment = await _paymentGateway.GetPaymentDetailsAsync(request.PaymentId);
        if (gatewayPayment.OrderId != payment.OrderId ||
            gatewayPayment.Amount != decimal.ToInt32(payment.Amount * 100m) ||
            !string.Equals(gatewayPayment.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailedAt = DateTime.UtcNow;
            await _paymentRepository.UpdateAsync(payment);
            throw new Application.Exceptions.ValidationException("Payment details do not match the order");
        }

        payment.PaymentId = gatewayPayment.Id;
        payment.Signature = request.Signature;
        payment.PaymentMethod = gatewayPayment.Method;
        payment.CardLastFour = gatewayPayment.CardLastFour;
        payment.CardIssuer = gatewayPayment.CardIssuer;

        if (gatewayPayment.Status == "captured")
        {
            payment.Status = PaymentStatus.Completed;
            payment.ProcessedAt = DateTime.UtcNow;
            await UpdateAdvertisementPaidAmountAsync(payment);
        }

        else if (gatewayPayment.Status == "failed")
        {
            payment.Status = PaymentStatus.Failed;
            payment.FailedAt = DateTime.UtcNow;
        }
        else
        {
            payment.Status = PaymentStatus.Processing;
        }

        await _paymentRepository.UpdateAsync(payment);
        _logger.LogInformation(
            "Payment status verified: {OrderId}, {PaymentId}, {Status}",
            payment.OrderId,
            request.PaymentId,
            payment.Status);

        return MapToPaymentDto(payment);
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

    public async Task<PaymentDto?> ProcessWebhookAsync(string jsonPayload, string signature)
    {
        if (!_paymentGateway.VerifyWebhookSignature(jsonPayload, signature))
            throw new Application.Exceptions.ValidationException("Webhook signature verification failed");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonPayload);
        }
        catch (JsonException ex)
        {
            throw new Application.Exceptions.ValidationException($"Invalid webhook payload: {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            var eventName = root.GetProperty("event").GetString();
            var targetStatus = eventName switch
            {
                "payment.captured" or "order.paid" => PaymentStatus.Completed,
                "payment.failed" => PaymentStatus.Failed,
                "payment.authorized" => PaymentStatus.Processing,
                _ => (PaymentStatus?)null
            };

            if (targetStatus == null)
                return null;

            var entity = root.GetProperty("payload").GetProperty("payment").GetProperty("entity");
            var orderId = entity.GetProperty("order_id").GetString();
            if (string.IsNullOrWhiteSpace(orderId))
                throw new Application.Exceptions.ValidationException("Webhook payment has no order ID");

            var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.OrderId == orderId);
            if (payment == null)
                throw new NotFoundException("Payment order not found");

            var amount = entity.GetProperty("amount").GetInt32();
            var currency = entity.GetProperty("currency").GetString();
            if (amount != decimal.ToInt32(payment.Amount * 100m) ||
                !string.Equals(currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
                throw new Application.Exceptions.ValidationException("Webhook payment details do not match the order");

            var wasCompleted = payment.Status == PaymentStatus.Completed;
            payment.PaymentId = entity.GetProperty("id").GetString() ?? payment.PaymentId;
            payment.PaymentMethod = GetStringOrEmpty(entity, "method");
            if (entity.TryGetProperty("card", out var card))
            {
                payment.CardLastFour = GetStringOrNull(card, "last4");
                payment.CardIssuer = GetStringOrNull(card, "issuer");
            }

            if (!(wasCompleted && targetStatus == PaymentStatus.Failed))
            {
                payment.Status = targetStatus.Value;
                if (targetStatus == PaymentStatus.Completed)
                {
                    payment.ProcessedAt ??= DateTime.UtcNow;
                    await UpdateAdvertisementPaidAmountAsync(payment);
                }
                else if (targetStatus == PaymentStatus.Failed)
                {
                    payment.FailedAt ??= DateTime.UtcNow;
                }
            }

            await _paymentRepository.UpdateAsync(payment);
            _logger.LogInformation(
                "Razorpay webhook processed: {OrderId}, event {EventName}",
                payment.OrderId,
                eventName);

            return MapToPaymentDto(payment);
        }
    }

    private async Task UpdateAdvertisementPaidAmountAsync(Payment payment)
    {
        if (!payment.AdvertisementId.HasValue)
            return;

        var advertisement = await _advertisementRepository.GetByIdAsync(payment.AdvertisementId.Value);
        if (advertisement == null)
            return;

        advertisement.PaymentId = payment.Id;
        advertisement.PaidAmount = payment.Amount;
        await _advertisementRepository.UpdateAsync(advertisement);
    }

    private static string GetStringOrEmpty(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetString()
            : null;
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
