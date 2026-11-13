using Microsoft.Extensions.Logging;
using Adrover.Data.Repositories;
using Adrover.Infrastructure.Services;
using Adrover.Domain.Entities;
using Adrover.Application.Exceptions;
using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Auth;

namespace Adrover.Application.Services;

/// <summary>
/// Implementation of user service for profile management
/// </summary>
public class UserService : IUserService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordService _passwordService;
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IPasswordService passwordService,
        IFileStorageService fileStorageService,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _passwordService = passwordService;
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    public async Task<UserProfileDto> GetCurrentUserAsync(Guid userId)
    {
        _logger.LogInformation("Fetching current user profile for user {userId}", userId);
        
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        if (!user.IsActive)
            throw new AuthenticationException("Account has been deactivated");

        return MapToUserProfileDto(user);
    }

    public async Task<UserProfileDto> GetUserAsync(Guid userId)
    {
        _logger.LogInformation("Fetching user profile for user {userId}", userId);
        
        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        return MapToUserProfileDto(user);
    }

    public async Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request)
    {
        _logger.LogInformation("Updating profile for user {userId}", userId);

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        if (!user.IsActive)
            throw new AuthenticationException("Account has been deactivated");

        user.FullName = request.Name;
        user.UpdatedAt = DateTime.UtcNow;

        var updated = await _userRepository.UpdateAsync(user);
        _logger.LogInformation("Profile updated successfully for user {userId}", userId);
        
        return MapToUserProfileDto(updated);
    }

    public async Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request)
    {
        _logger.LogInformation("Changing password for user {userId}", userId);

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        if (!user.IsActive)
            throw new AuthenticationException("Account has been deactivated");

        if (!_passwordService.VerifyPassword(request.CurrentPassword, user.PasswordHash))
            throw new AuthenticationException("Current password is incorrect");

        user.PasswordHash = _passwordService.HashPassword(request.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);
        
        _logger.LogInformation("Password changed successfully for user {userId}", userId);
        return true;
    }

    public async Task<ProfilePictureUploadResponse> UploadProfilePictureAsync(Guid userId, Stream fileStream, string fileName)
    {
        _logger.LogInformation("Uploading profile picture for user {userId}", userId);

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        if (!user.IsActive)
            throw new AuthenticationException("Account has been deactivated");

        // Validate file
        if (fileStream.Length == 0)
            throw new ValidationException("File is empty");

        if (fileStream.Length > 5 * 1024 * 1024) // 5MB
            throw new ValidationException("File size exceeds 5MB limit");

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension != ".jpg" && extension != ".jpeg" && extension != ".png" && extension != ".gif")
            throw new ValidationException("Only image files (jpg, jpeg, png, gif) are allowed");

        try
        {
            var fileUrl = await _fileStorageService.UploadFileAsync(
                "user-profiles",
                $"{userId}/{fileName}",
                fileStream
            );

            user.ProfileImageUrl = fileUrl;
            user.UpdatedAt = DateTime.UtcNow;
            await _userRepository.UpdateAsync(user);

            _logger.LogInformation("Profile picture uploaded successfully for user {userId}", userId);

            return new ProfilePictureUploadResponse
            {
                FileUrl = fileUrl,
                FileName = fileName,
                FileSizeBytes = fileStream.Length,
                ContentType = GetContentType(extension)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading profile picture for user {userId}", userId);
            throw new Exceptions.ApplicationException("Failed to upload profile picture", ex);
        }
    }

    public async Task<bool> DeactivateAccountAsync(Guid userId)
    {
        _logger.LogInformation("Deactivating account for user {userId}", userId);

        var user = await _userRepository.GetByIdAsync(userId);
        if (user == null)
            throw new NotFoundException("User not found");

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user);

        _logger.LogInformation("Account deactivated for user {userId}", userId);
        return true;
    }

    private UserProfileDto MapToUserProfileDto(User user)
    {
        return new UserProfileDto
        {
            Id = user.Id,
            Name = user.FullName,
            Email = user.Email,
            Phone = user.Phone,
            Role = user.Role.ToString(),
            ProfilePictureUrl = user.ProfileImageUrl,
            VehicleNumber = null, // TODO: Add VehicleNumber field to User entity if needed
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt
        };
    }

    private static string GetContentType(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            _ => "application/octet-stream"
        };
    }
}
