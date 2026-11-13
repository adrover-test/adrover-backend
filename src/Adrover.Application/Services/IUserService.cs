using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Auth;

namespace Adrover.Application.Services;

/// <summary>
/// Service for user profile management
/// </summary>
public interface IUserService
{
    /// <summary>
    /// Get current authenticated user profile
    /// </summary>
    Task<UserProfileDto> GetCurrentUserAsync(Guid userId);

    /// <summary>
    /// Get user by ID
    /// </summary>
    Task<UserProfileDto> GetUserAsync(Guid userId);

    /// <summary>
    /// Update user profile
    /// </summary>
    Task<UserProfileDto> UpdateProfileAsync(Guid userId, UpdateUserProfileRequest request);

    /// <summary>
    /// Change user password
    /// </summary>
    Task<bool> ChangePasswordAsync(Guid userId, ChangePasswordRequest request);

    /// <summary>
    /// Upload profile picture
    /// </summary>
    Task<ProfilePictureUploadResponse> UploadProfilePictureAsync(Guid userId, Stream fileStream, string fileName);

    /// <summary>
    /// Delete user account (soft delete)
    /// </summary>
    Task<bool> DeactivateAccountAsync(Guid userId);
}
