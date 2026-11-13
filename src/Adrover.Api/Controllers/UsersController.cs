using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using FluentValidation;
using Adrover.Application.Services;
using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Auth;
using Adrover.Application.Exceptions;

namespace Adrover.Api.Controllers;

/// <summary>
/// Controller for user profile management
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly IUserService _userService;
    private readonly IValidator<UpdateUserProfileRequest> _updateProfileValidator;
    private readonly IValidator<ChangePasswordRequest> _changePasswordValidator;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        IUserService userService,
        IValidator<UpdateUserProfileRequest> updateProfileValidator,
        IValidator<ChangePasswordRequest> changePasswordValidator,
        ILogger<UsersController> logger)
    {
        _userService = userService;
        _updateProfileValidator = updateProfileValidator;
        _changePasswordValidator = changePasswordValidator;
        _logger = logger;
    }

    /// <summary>
    /// Get current authenticated user profile
    /// </summary>
    /// <returns>Current user profile details</returns>
    [HttpGet("me")]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> GetCurrentUser()
    {
        try
        {
            var userId = GetUserIdFromToken();
            _logger.LogInformation("Fetching current user profile for user {userId}", userId);

            var user = await _userService.GetCurrentUserAsync(userId);
            
            return Ok(new ApiResponse<UserProfileDto>
            {
                Success = true,
                Data = user,
                Message = "User profile retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching current user profile");
            throw;
        }
    }

    /// <summary>
    /// Get user profile by ID
    /// </summary>
    /// <param name="id">User ID</param>
    /// <returns>User profile details</returns>
    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> GetUserById(Guid id)
    {
        try
        {
            _logger.LogInformation("Fetching user profile for user {userId}", id);

            var user = await _userService.GetUserAsync(id);
            
            return Ok(new ApiResponse<UserProfileDto>
            {
                Success = true,
                Data = user,
                Message = "User profile retrieved successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("User not found: {id}", id);
            return NotFound(new ApiResponse<UserProfileDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching user profile for user {userId}", id);
            throw;
        }
    }

    /// <summary>
    /// Update current user profile
    /// </summary>
    /// <param name="request">Profile update request</param>
    /// <returns>Updated user profile</returns>
    [HttpPut("me")]
    public async Task<ActionResult<ApiResponse<UserProfileDto>>> UpdateProfile(
        [FromBody] UpdateUserProfileRequest request)
    {
        var userId = Guid.Empty;
        try
        {
            userId = GetUserIdFromToken();
            
            var validationResult = await _updateProfileValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Validation failed for update profile request");
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            _logger.LogInformation("Updating profile for user {userId}", userId);

            var updatedUser = await _userService.UpdateProfileAsync(userId, request);
            
            return Ok(new ApiResponse<UserProfileDto>
            {
                Success = true,
                Data = updatedUser,
                Message = "Profile updated successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("User not found for profile update");
            return NotFound(new ApiResponse<UserProfileDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (FluentValidation.ValidationException ex)
        {
            _logger.LogWarning("Validation error during profile update: {errors}", 
                string.Join(", ", ex.Errors.Select(e => e.ErrorMessage)));
            return BadRequest(new ApiResponse<UserProfileDto>
            {
                Success = false,
                Message = "Validation failed",
                Errors = ex.Errors.Select(e => e.ErrorMessage).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating profile for user {userId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Change user password
    /// </summary>
    /// <param name="request">Password change request</param>
    /// <returns>Success message</returns>
    [HttpPost("change-password")]
    public async Task<ActionResult<ApiResponse<object>>> ChangePassword(
        [FromBody] ChangePasswordRequest request)
    {
        var userId = Guid.Empty;
        try
        {
            userId = GetUserIdFromToken();
            
            var validationResult = await _changePasswordValidator.ValidateAsync(request);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Validation failed for change password request");
                throw new FluentValidation.ValidationException(validationResult.Errors);
            }

            _logger.LogInformation("Changing password for user {userId}", userId);

            await _userService.ChangePasswordAsync(userId, request);
            
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Password changed successfully"
            });
        }
        catch (AuthenticationException ex)
        {
            _logger.LogWarning("Authentication error during password change: {message}", ex.Message);
            return Unauthorized(new ApiResponse<object>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("User not found for password change");
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (FluentValidation.ValidationException ex)
        {
            _logger.LogWarning("Validation error during password change: {errors}", 
                string.Join(", ", ex.Errors.Select(e => e.ErrorMessage)));
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = "Validation failed",
                Errors = ex.Errors.Select(e => e.ErrorMessage).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error changing password for user {userId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Upload user profile picture
    /// </summary>
    /// <returns>File upload response with URL</returns>
    [HttpPost("profile-picture")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<ApiResponse<ProfilePictureUploadResponse>>> UploadProfilePicture()
    {
        var userId = Guid.Empty;
        try
        {
            userId = GetUserIdFromToken();
            
            if (Request.Form.Files.Count == 0)
            {
                return BadRequest(new ApiResponse<ProfilePictureUploadResponse>
                {
                    Success = false,
                    Message = "No file provided"
                });
            }

            var file = Request.Form.Files[0];

            if (file.Length == 0)
            {
                return BadRequest(new ApiResponse<ProfilePictureUploadResponse>
                {
                    Success = false,
                    Message = "File is empty"
                });
            }

            _logger.LogInformation("Uploading profile picture for user {userId}, file size: {size} bytes", 
                userId, file.Length);

            await using var stream = file.OpenReadStream();
            var response = await _userService.UploadProfilePictureAsync(userId, stream, file.FileName);
            
            return Ok(new ApiResponse<ProfilePictureUploadResponse>
            {
                Success = true,
                Data = response,
                Message = "Profile picture uploaded successfully"
            });
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error during file upload: {message}", ex.Message);
            return BadRequest(new ApiResponse<ProfilePictureUploadResponse>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("User not found for profile picture upload");
            return NotFound(new ApiResponse<ProfilePictureUploadResponse>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading profile picture for user {userId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Extract user ID from JWT token
    /// </summary>
    private Guid GetUserIdFromToken()
    {
        var userIdClaim = User.FindFirst("sub") ?? User.FindFirst("nameid");
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
        {
            throw new AuthenticationException("Invalid user token");
        }
        return userId;
    }
}
