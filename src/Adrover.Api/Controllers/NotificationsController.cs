using Adrover.Application.DTOs;
using Adrover.Application.Services;
using Adrover.Application.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Adrover.Api.Controllers;

/// <summary>
/// Notification management endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(INotificationService notificationService, ILogger<NotificationsController> logger)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// Get user notifications with pagination
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetNotifications([FromQuery] int skip = 0, [FromQuery] int take = 10)
    {
        try
        {
            var userId = GetUserIdFromToken();
            var result = await _notificationService.GetNotificationsAsync(userId, skip, take);
            
            _logger.LogInformation("Retrieved notifications for user {UserId}", userId);
            
            return Ok(new ApiResponse<NotificationListResponse>
            {
                Success = true,
                Data = result,
                Message = "Notifications retrieved successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("User not found: {Message}", ex.Message);
            return NotFound(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "USER_NOT_FOUND",
                    Message = ex.Message
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving notifications");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving notifications"
                }
            });
        }
    }

    /// <summary>
    /// Mark a notification as read
    /// </summary>
    [HttpPut("{notificationId}/read")]
    public async Task<IActionResult> MarkAsRead([FromRoute] Guid notificationId)
    {
        try
        {
            var userId = GetUserIdFromToken();
            var result = await _notificationService.MarkAsReadAsync(userId, notificationId);
            
            _logger.LogInformation("Notification {NotificationId} marked as read for user {UserId}", notificationId, userId);
            
            return Ok(new ApiResponse<NotificationDto>
            {
                Success = true,
                Data = result,
                Message = "Notification marked as read"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Notification not found: {Message}", ex.Message);
            return NotFound(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "NOTIFICATION_NOT_FOUND",
                    Message = ex.Message
                }
            });
        }
        catch (AuthorizationException ex)
        {
            _logger.LogWarning("Unauthorized access: {Message}", ex.Message);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking notification as read");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error marking notification as read"
                }
            });
        }
    }

    /// <summary>
    /// Mark all notifications as read
    /// </summary>
    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        try
        {
            var userId = GetUserIdFromToken();
            var count = await _notificationService.MarkAllAsReadAsync(userId);
            
            _logger.LogInformation("Marked {Count} notifications as read for user {UserId}", count, userId);
            
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = new { count },
                Message = "All notifications marked as read"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("User not found: {Message}", ex.Message);
            return NotFound(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "USER_NOT_FOUND",
                    Message = ex.Message
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking all notifications as read");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error marking all notifications as read"
                }
            });
        }
    }

    /// <summary>
    /// Delete a notification
    /// </summary>
    [HttpDelete("{notificationId}")]
    public async Task<IActionResult> DeleteNotification([FromRoute] Guid notificationId)
    {
        try
        {
            var userId = GetUserIdFromToken();
            await _notificationService.DeleteNotificationAsync(userId, notificationId);
            
            _logger.LogInformation("Notification {NotificationId} deleted for user {UserId}", notificationId, userId);
            
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = new { },
                Message = "Notification deleted successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Notification not found: {Message}", ex.Message);
            return NotFound(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "NOTIFICATION_NOT_FOUND",
                    Message = ex.Message
                }
            });
        }
        catch (AuthorizationException ex)
        {
            _logger.LogWarning("Unauthorized access: {Message}", ex.Message);
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting notification");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error deleting notification"
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
