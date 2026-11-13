using Adrover.Application.DTOs;

namespace Adrover.Application.Services;

/// <summary>
/// Service interface for notification operations
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Get notifications for a user with pagination
    /// </summary>
    Task<NotificationListResponse> GetNotificationsAsync(Guid userId, int skip = 0, int take = 10);
    
    /// <summary>
    /// Mark notification as read
    /// </summary>
    Task<NotificationDto> MarkAsReadAsync(Guid userId, Guid notificationId);
    
    /// <summary>
    /// Mark all notifications as read for a user
    /// </summary>
    Task<int> MarkAllAsReadAsync(Guid userId);
    
    /// <summary>
    /// Delete a notification
    /// </summary>
    Task<bool> DeleteNotificationAsync(Guid userId, Guid notificationId);
    
    /// <summary>
    /// Create a notification for a user
    /// </summary>
    Task<NotificationDto> CreateNotificationAsync(Guid userId, string title, string message, string type, Guid? relatedResourceId = null);
}
