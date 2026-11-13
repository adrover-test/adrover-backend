using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Advertisement;
using Adrover.Application.Services;
using Adrover.Application.Exceptions;
using Adrover.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Adrover.Api.Controllers;

/// <summary>
/// Administrative endpoints for SuperAdmin users
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "SuperAdminOnly")]
public class AdminController : ControllerBase
{
    private readonly IAdvertisementService _advertisementService;
    private readonly IPaymentService _paymentService;
    private readonly IUserService _userService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IAdvertisementService advertisementService,
        IPaymentService paymentService,
        IUserService userService,
        ILogger<AdminController> logger)
    {
        _advertisementService = advertisementService;
        _paymentService = paymentService;
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Get all pending advertisement approvals
    /// </summary>
    [HttpGet("advertisements/pending")]
    public async Task<IActionResult> GetPendingAdvertisements([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            var adminId = GetUserIdFromToken();
            
            var result = await _advertisementService.GetPendingApprovalsAsync(pageNumber, pageSize);
            
            _logger.LogInformation("Admin {AdminId} retrieved {Count} pending advertisements", adminId, result.Items.Count);
            
            return Ok(new ApiResponse<AdvertisementListResponse>
            {
                Success = true,
                Data = result,
                Message = "Pending advertisements retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pending advertisements");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving pending advertisements"
                }
            });
        }
    }

    /// <summary>
    /// Approve an advertisement for payment
    /// </summary>
    [HttpPost("advertisements/{advertisementId}/approve")]
    public async Task<IActionResult> ApproveAdvertisement(
        [FromRoute] Guid advertisementId,
        [FromBody] ApprovalDecisionRequest request)
    {
        try
        {
            var adminId = GetUserIdFromToken();

            if (!ModelState.IsValid)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_REQUEST",
                        Message = "Invalid request data"
                    }
                });
            }

            var result = await _advertisementService.ApproveAdvertisementAsync(
                advertisementId,
                adminId.ToString());
            
            _logger.LogInformation("Admin {AdminId} approved advertisement {AdId}", adminId, advertisementId);
            
            return Ok(new ApiResponse<AdvertisementDto>
            {
                Success = true,
                Data = result,
                Message = "Advertisement approved successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {Message}", ex.Message);
            return NotFound(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "ADVERTISEMENT_NOT_FOUND",
                    Message = ex.Message
                }
            });
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error: {Message}", ex.Message);
            return BadRequest(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "VALIDATION_ERROR",
                    Message = ex.Message
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving advertisement");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error approving advertisement"
                }
            });
        }
    }

    /// <summary>
    /// Reject an advertisement with reason
    /// </summary>
    [HttpPost("advertisements/{advertisementId}/reject")]
    public async Task<IActionResult> RejectAdvertisement(
        [FromRoute] Guid advertisementId,
        [FromBody] ApprovalDecisionRequest request)
    {
        try
        {
            var adminId = GetUserIdFromToken();

            if (!ModelState.IsValid || string.IsNullOrWhiteSpace(request.RejectionReason))
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_REQUEST",
                        Message = "Rejection reason is required"
                    }
                });
            }

            var result = await _advertisementService.RejectAdvertisementAsync(
                advertisementId,
                request.RejectionReason,
                adminId.ToString());
            
            _logger.LogInformation("Admin {AdminId} rejected advertisement {AdId}: {Reason}", 
                adminId, advertisementId, request.RejectionReason);
            
            return Ok(new ApiResponse<AdvertisementDto>
            {
                Success = true,
                Data = result,
                Message = "Advertisement rejected successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {Message}", ex.Message);
            return NotFound(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "ADVERTISEMENT_NOT_FOUND",
                    Message = ex.Message
                }
            });
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error: {Message}", ex.Message);
            return BadRequest(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "VALIDATION_ERROR",
                    Message = ex.Message
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting advertisement");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error rejecting advertisement"
                }
            });
        }
    }

    /// <summary>
    /// Get payment analytics and statistics
    /// </summary>
    [HttpGet("payments/analytics")]
    public async Task<IActionResult> GetPaymentAnalytics(
        [FromQuery] DateTime? startDate,
        [FromQuery] DateTime? endDate)
    {
        try
        {
            var adminId = GetUserIdFromToken();

            if (!startDate.HasValue) startDate = DateTime.UtcNow.AddMonths(-1);
            if (!endDate.HasValue) endDate = DateTime.UtcNow;

            if (startDate > endDate)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_DATE_RANGE",
                        Message = "Start date must be before end date"
                    }
                });
            }

            // This would need implementation in PaymentService
            // For now, return a placeholder response
            var analytics = new
            {
                totalPayments = 0,
                totalAmount = 0m,
                averageAmount = 0m,
                completedCount = 0,
                failedCount = 0,
                pendingCount = 0,
                startDate = startDate,
                endDate = endDate
            };

            _logger.LogInformation("Admin {AdminId} retrieved payment analytics from {Start} to {End}", 
                adminId, startDate, endDate);
            
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = analytics,
                Message = "Payment analytics retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment analytics");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving payment analytics"
                }
            });
        }
    }

    /// <summary>
    /// Get advertisement analytics and statistics
    /// </summary>
    [HttpGet("advertisements/analytics")]
    public async Task<IActionResult> GetAdvertisementAnalytics([FromQuery] Guid? advertisementId = null)
    {
        try
        {
            var adminId = GetUserIdFromToken();

            if (!advertisementId.HasValue || advertisementId == Guid.Empty)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_REQUEST",
                        Message = "Advertisement ID is required"
                    }
                });
            }

            var analytics = await _advertisementService.GetAnalyticsAsync(advertisementId.Value);

            _logger.LogInformation("Admin {AdminId} retrieved analytics for advertisement {AdId}", 
                adminId, advertisementId);
            
            return Ok(new ApiResponse<AdvertisementAnalyticsDto>
            {
                Success = true,
                Data = analytics,
                Message = "Advertisement analytics retrieved successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {Message}", ex.Message);
            return NotFound(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "ADVERTISEMENT_NOT_FOUND",
                    Message = ex.Message
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving advertisement analytics");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving advertisement analytics"
                }
            });
        }
    }

    /// <summary>
    /// Get user management statistics
    /// </summary>
    [HttpGet("users/statistics")]
    public async Task<IActionResult> GetUserStatistics()
    {
        try
        {
            var adminId = GetUserIdFromToken();

            // Placeholder for user statistics
            var stats = new
            {
                totalUsers = 0,
                activeUsers = 0,
                inactiveUsers = 0,
                clientsCount = 0,
                driversCount = 0,
                lastUpdated = DateTime.UtcNow
            };

            _logger.LogInformation("Admin {AdminId} retrieved user statistics", adminId);
            
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = stats,
                Message = "User statistics retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user statistics");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving user statistics"
                }
            });
        }
    }

    /// <summary>
    /// Helper method to extract user ID from JWT token claims
    /// </summary>
    private Guid GetUserIdFromToken()
    {
        var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            throw new UnauthorizedAccessException("Invalid user token");
        }
        return userId;
    }
}
