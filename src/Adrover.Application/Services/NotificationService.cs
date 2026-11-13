using Adrover.Application.DTOs;
using Adrover.Application.Exceptions;
using Adrover.Data.Repositories;
using Adrover.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Adrover.Application.Services;

/// <summary>
/// Service for notification operations
/// </summary>
public class NotificationService : INotificationService
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IRepository<User> _userRepository;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(
        INotificationRepository notificationRepository,
        IRepository<User> userRepository,
        ILogger<NotificationService> logger)
    {
        _notificationRepository = notificationRepository;
        _userRepository = userRepository;
        _logger = logger;
    }

    public async Task<NotificationListResponse> GetNotificationsAsync(Guid userId, int skip = 0, int take = 10)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found: {UserId}", userId);
            throw new NotFoundException("User not found");
        }

        // Validate pagination
        if (skip < 0) skip = 0;
        if (take <= 0) take = 10;
        if (take > 100) take = 100;

        var notifications = await _notificationRepository.FindAsync(n => n.UserId == userId);
        
        var total = notifications.Count();
        var unread = notifications.Count(n => !n.IsRead);
        
        var items = notifications
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(MapToNotificationDto)
            .ToList();

        _logger.LogInformation("Retrieved notifications for user {UserId}: {Count} items, {UnreadCount} unread", 
            userId, items.Count, unread);

        return new NotificationListResponse
        {
            Items = items,
            Total = total,
            Unread = unread,
            Skip = skip,
            Take = take
        };
    }

    public async Task<NotificationDto> MarkAsReadAsync(Guid userId, Guid notificationId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found: {UserId}", userId);
            throw new NotFoundException("User not found");
        }

        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification == null)
        {
            _logger.LogWarning("Notification not found: {NotificationId}", notificationId);
            throw new NotFoundException("Notification not found");
        }

        if (notification.UserId != userId)
        {
            _logger.LogWarning("Unauthorized access to notification {NotificationId} by user {UserId}", 
                notificationId, userId);
            throw new AuthorizationException("You do not have permission to access this notification");
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _notificationRepository.UpdateAsync(notification);
            _logger.LogInformation("Notification {NotificationId} marked as read", notificationId);
        }

        return MapToNotificationDto(notification);
    }

    public async Task<int> MarkAllAsReadAsync(Guid userId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found: {UserId}", userId);
            throw new NotFoundException("User not found");
        }

        var notifications = await _notificationRepository.FindAsync(n => n.UserId == userId && !n.IsRead);
        
        int count = 0;
        foreach (var notification in notifications)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            await _notificationRepository.UpdateAsync(notification);
            count++;
        }

        _logger.LogInformation("Marked {Count} notifications as read for user {UserId}", count, userId);
        return count;
    }

    public async Task<bool> DeleteNotificationAsync(Guid userId, Guid notificationId)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found: {UserId}", userId);
            throw new NotFoundException("User not found");
        }

        var notification = await _notificationRepository.GetByIdAsync(notificationId);
        if (notification == null)
        {
            _logger.LogWarning("Notification not found: {NotificationId}", notificationId);
            throw new NotFoundException("Notification not found");
        }

        if (notification.UserId != userId)
        {
            _logger.LogWarning("Unauthorized access to notification {NotificationId} by user {UserId}", 
                notificationId, userId);
            throw new AuthorizationException("You do not have permission to delete this notification");
        }

        await _notificationRepository.DeleteAsync(notification);
        _logger.LogInformation("Notification {NotificationId} deleted", notificationId);

        return true;
    }

    public async Task<NotificationDto> CreateNotificationAsync(Guid userId, string title, string message, string type, Guid? relatedResourceId = null)
    {
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found: {UserId}", userId);
            throw new NotFoundException("User not found");
        }

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
        {
            _logger.LogWarning("Invalid notification data - title and message are required");
            throw new ValidationException("Title and message are required");
        }

        var notification = new Notification
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Title = title,
            Message = message,
            Type = type,
            RelatedResourceId = relatedResourceId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        await _notificationRepository.AddAsync(notification);
        _logger.LogInformation("Notification created for user {UserId}: {Title}", userId, title);

        return MapToNotificationDto(notification);
    }

    private static NotificationDto MapToNotificationDto(Notification notification)
    {
        return new NotificationDto
        {
            NotificationId = notification.Id,
            Title = notification.Title,
            Message = notification.Message,
            Type = notification.Type,
            IsRead = notification.IsRead,
            RelatedId = notification.RelatedResourceId,
            CreatedAt = notification.CreatedAt,
            ReadAt = notification.ReadAt
        };
    }
}
