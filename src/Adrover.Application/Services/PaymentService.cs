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
            Id = payment.Id,
            OrderId = payment.OrderId,
            Amount = payment.Amount,
            AmountInPaise = decimal.ToInt32(amountInPaise),
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

        var isValid = _paymentGateway.VerifyPaymentSignature(
            payment.OrderId,
            request.PaymentId,
            request.Signature);

        if (!isValid)
            throw new Application.Exceptions.ValidationException("Payment signature verification failed");

        var gatewayPayment = await _paymentGateway.GetPaymentDetailsAsync(request.PaymentId);
        if (gatewayPayment.Id != request.PaymentId)
            throw new Application.Exceptions.ValidationException("Payment ID does not match the verification request");

        ValidatePaymentDetails(payment, gatewayPayment);
        if ((payment.Status is PaymentStatus.Completed or PaymentStatus.Refunded) &&
            payment.PaymentId != gatewayPayment.Id)
            throw new Application.Exceptions.ValidationException("The order has already been paid by a different payment");

        payment.Signature = request.Signature;
        await SynchronizePaymentAsync(payment, gatewayPayment);
        _logger.LogInformation(
            "Payment status verified: {OrderId}, {PaymentId}, {Status}",
            payment.OrderId,
            request.PaymentId,
            payment.Status);

        return MapToPaymentDto(payment);
    }

    public async Task<PaymentDto> GetPaymentAsync(Guid userId, Guid id)
    {
        var payment = await _paymentRepository.GetByIdAsync(id);
        if (payment == null)
            throw new NotFoundException("Payment not found");

        if (payment.UserId != userId)
            throw new AuthorizationException("You can only view your own payments");

        return MapToPaymentDto(payment);
    }

    public async Task<PaymentDto> GetOrderStatusAsync(Guid userId, string orderId)
    {
        var payment = await _paymentRepository.FirstOrDefaultAsync(p => p.OrderId == orderId);
        if (payment == null)
            throw new NotFoundException("Payment order not found");

        if (payment.UserId != userId)
            throw new AuthorizationException("You can only view your own payments");

        await RefreshPaymentAsync(payment);
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
            if (eventName is not ("payment.captured" or "order.paid" or "payment.failed" or "payment.authorized"))
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

            await RefreshPaymentAsync(payment);
            _logger.LogInformation(
                "Razorpay webhook processed: {OrderId}, event {EventName}",
                payment.OrderId,
                eventName);

            return MapToPaymentDto(payment);
        }
    }

    private async Task RefreshPaymentAsync(Payment payment)
    {
        RazorpayPaymentDetails? gatewayPayment;
        if ((payment.Status is PaymentStatus.Completed or PaymentStatus.Refunded) &&
            !string.IsNullOrEmpty(payment.PaymentId))
        {
            gatewayPayment = await _paymentGateway.GetPaymentDetailsAsync(payment.PaymentId);
            if (gatewayPayment.Id != payment.PaymentId)
                throw new Application.Exceptions.ValidationException("Payment ID does not match the stored payment");
        }
        else
        {
            var attempts = await _paymentGateway.GetOrderPaymentsAsync(payment.OrderId);
            gatewayPayment = attempts.OrderBy(p => p.Status switch
            {
                "captured" => 0,
                "refunded" => 1,
                "authorized" => 2,
                "created" => 3,
                "failed" => 4,
                _ => 5
            }).FirstOrDefault();
        }

        if (gatewayPayment != null)
            await SynchronizePaymentAsync(payment, gatewayPayment);
    }

    private static void ValidatePaymentDetails(Payment payment, RazorpayPaymentDetails gatewayPayment)
    {
        if (string.IsNullOrWhiteSpace(gatewayPayment.Id) ||
            gatewayPayment.OrderId != payment.OrderId ||
            gatewayPayment.Amount != decimal.ToInt32(payment.Amount * 100m) ||
            !string.Equals(gatewayPayment.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase))
            throw new Application.Exceptions.ValidationException("Payment details do not match the order");
    }

    private async Task SynchronizePaymentAsync(Payment payment, RazorpayPaymentDetails gatewayPayment)
    {
        ValidatePaymentDetails(payment, gatewayPayment);
        if (payment.Status == PaymentStatus.Refunded ||
            (payment.Status == PaymentStatus.Completed && gatewayPayment.Status is not ("captured" or "refunded")))
            return;

        if (gatewayPayment.Status == "authorized")
        {
            var captured = await _paymentGateway.CapturePaymentAsync(
                gatewayPayment.Id, decimal.ToInt32(payment.Amount * 100m), payment.Currency);
            if (captured.Id != gatewayPayment.Id)
                throw new Application.Exceptions.ValidationException("Captured payment ID does not match the authorized payment");

            ValidatePaymentDetails(payment, captured);
            gatewayPayment = captured;
        }

        var status = gatewayPayment.Status switch
        {
            "captured" => PaymentStatus.Completed,
            "refunded" => PaymentStatus.Refunded,
            "failed" => PaymentStatus.Failed,
            "created" or "authorized" => PaymentStatus.Processing,
            _ => throw new Application.Exceptions.ValidationException($"Unsupported Razorpay payment status: {gatewayPayment.Status}")
        };

        payment.PaymentId = gatewayPayment.Id;
        payment.PaymentMethod = gatewayPayment.Method;
        payment.CardLastFour = gatewayPayment.CardLastFour;
        payment.CardIssuer = gatewayPayment.CardIssuer;
        payment.Status = status;
        if (status == PaymentStatus.Completed)
        {
            payment.ProcessedAt ??= DateTime.UtcNow;
            payment.FailedAt = null;
            await UpdateAdvertisementPaidAmountAsync(payment);
        }
        else if (status == PaymentStatus.Failed)
        {
            payment.FailedAt ??= DateTime.UtcNow;
        }
        else if (status == PaymentStatus.Processing)
        {
            payment.FailedAt = null;
        }

        await _paymentRepository.UpdateAsync(payment);
    }

    private async Task UpdateAdvertisementPaidAmountAsync(Payment payment)
    {
        if (!payment.AdvertisementId.HasValue)
            return;

        var advertisement = await _advertisementRepository.GetByIdAsync(payment.AdvertisementId.Value);
        if (advertisement == null)
            throw new NotFoundException("Advertisement for the payment not found");

        advertisement.PaymentId = payment.Id;
        advertisement.PaidAmount = payment.Amount;
        await _advertisementRepository.UpdateAsync(advertisement);
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
