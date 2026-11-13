using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Payment;
using Adrover.Application.Exceptions;
using Adrover.Application.Services;
using System.Security.Claims;

namespace Adrover.Api.Controllers;

/// <summary>
/// Payment management endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly ILogger<PaymentsController> _logger;

    public PaymentsController(
        IPaymentService paymentService,
        ILogger<PaymentsController> logger)
    {
        _paymentService = paymentService;
        _logger = logger;
    }

    /// <summary>
    /// Create a payment order
    /// </summary>
    [HttpPost("create-order")]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
    {
        Guid userId = Guid.Empty;
        try
        {
            userId = GetUserIdFromToken();
            var order = await _paymentService.CreateOrderAsync(userId, request);

            return Ok(new ApiResponse<PaymentOrderDto>
            {
                Success = true,
                Data = order,
                Message = "Order created successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Resource not found: {message}", ex.Message);
            return NotFound(new ApiResponse<PaymentOrderDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (AuthorizationException ex)
        {
            _logger.LogWarning("Authorization failed: {message}", ex.Message);
            return Forbid();
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<PaymentOrderDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment order for user: {UserId}", userId);
            return StatusCode(500, new ApiResponse<PaymentOrderDto>
            {
                Success = false,
                Message = "An error occurred while creating the order"
            });
        }
    }

    /// <summary>
    /// Verify a payment after successful Razorpay payment
    /// </summary>
    [HttpPost("verify")]
    public async Task<IActionResult> VerifyPayment([FromBody] VerifyPaymentRequest request)
    {
        Guid userId = Guid.Empty;
        try
        {
            userId = GetUserIdFromToken();
            var payment = await _paymentService.VerifyPaymentAsync(userId, request);

            return Ok(new ApiResponse<PaymentDto>
            {
                Success = true,
                Data = payment,
                Message = "Payment verified successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Payment not found: {message}", ex.Message);
            return NotFound(new ApiResponse<PaymentDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (AuthorizationException ex)
        {
            _logger.LogWarning("Authorization failed for user: {UserId}", userId);
            return Forbid();
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<PaymentDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying payment for user: {UserId}", userId);
            return StatusCode(500, new ApiResponse<PaymentDto>
            {
                Success = false,
                Message = "An error occurred while verifying the payment"
            });
        }
    }

    /// <summary>
    /// Get a specific payment by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPayment(Guid id)
    {
        Guid userId = Guid.Empty;
        try
        {
            userId = GetUserIdFromToken();
            var payment = await _paymentService.GetPaymentAsync(id);

            return Ok(new ApiResponse<PaymentDto>
            {
                Success = true,
                Data = payment,
                Message = "Payment retrieved successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Payment not found: {id}", id);
            return NotFound(new ApiResponse<PaymentDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment: {PaymentId}", id);
            return StatusCode(500, new ApiResponse<PaymentDto>
            {
                Success = false,
                Message = "An error occurred while retrieving the payment"
            });
        }
    }

    /// <summary>
    /// Get payment history for the current user
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetPaymentHistory([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        Guid userId = Guid.Empty;
        try
        {
            userId = GetUserIdFromToken();
            var history = await _paymentService.GetPaymentHistoryAsync(userId, pageNumber, pageSize);

            return Ok(new ApiResponse<PaymentHistoryResponse>
            {
                Success = true,
                Data = history,
                Message = $"Retrieved {history.Items.Count} payments"
            });
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<PaymentHistoryResponse>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment history for user: {UserId}", userId);
            return StatusCode(500, new ApiResponse<PaymentHistoryResponse>
            {
                Success = false,
                Message = "An error occurred while retrieving payment history"
            });
        }
    }

    /// <summary>
    /// Razorpay webhook endpoint for payment notifications
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> Webhook()
    {
        try
        {
            using var reader = new StreamReader(Request.Body);
            var jsonPayload = await reader.ReadToEndAsync();
            var signature = Request.Headers["X-Razorpay-Signature"].ToString();

            if (string.IsNullOrEmpty(jsonPayload) || string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("Webhook received with missing payload or signature");
                return BadRequest("Missing payload or signature");
            }

            var payment = await _paymentService.ProcessWebhookAsync(jsonPayload, signature);

            _logger.LogInformation("Webhook processed successfully for order: {OrderId}", payment.OrderId);

            return Ok(new { success = true, message = "Webhook processed" });
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Webhook validation error: {message}", ex.Message);
            return BadRequest(new { success = false, message = ex.Message });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Webhook resource not found: {message}", ex.Message);
            return NotFound(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook");
            return StatusCode(500, new { success = false, message = "Webhook processing failed" });
        }
    }

    private Guid GetUserIdFromToken()
    {
        var userIdClaim = User.FindFirst("sub") ?? User.FindFirst("nameid");
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            throw new AuthenticationException("Invalid or missing user ID in token");

        return userId;
    }
}
