using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Advertisement;
using Adrover.Application.Exceptions;
using Adrover.Application.Services;
using System.Security.Claims;

namespace Adrover.Api.Controllers;

/// <summary>
/// Advertisement management endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AdvertisementsController : ControllerBase
{
    private readonly IAdvertisementService _advertisementService;
    private readonly ILogger<AdvertisementsController> _logger;

    public AdvertisementsController(
        IAdvertisementService advertisementService,
        ILogger<AdvertisementsController> logger)
    {
        _advertisementService = advertisementService;
        _logger = logger;
    }

    /// <summary>
    /// Get advertisement by ID
    /// </summary>
    [HttpGet("{id}")]
    [AllowAnonymous]
    public async Task<IActionResult> GetAdvertisement(Guid id)
    {
        Guid advertisementId = Guid.Empty;
        try
        {
            advertisementId = id;
            var advertisement = await _advertisementService.GetAdvertisementAsync(id);

            return Ok(new ApiResponse<AdvertisementDto>
            {
                Success = true,
                Data = advertisement,
                Message = "Advertisement retrieved successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {AdvertisementId}", advertisementId);
            return NotFound(new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving advertisement: {AdvertisementId}", advertisementId);
            return StatusCode(500, new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = "An error occurred while retrieving the advertisement"
            });
        }
    }

    /// <summary>
    /// Get client's advertisements with pagination
    /// </summary>
    [HttpGet("my-ads")]
    public async Task<IActionResult> GetMyAdvertisements([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        Guid clientId = Guid.Empty;
        try
        {
            clientId = GetUserIdFromToken();
            var result = await _advertisementService.GetClientAdvertisementsAsync(clientId, pageNumber, pageSize);

            return Ok(new ApiResponse<AdvertisementListResponse>
            {
                Success = true,
                Data = result,
                Message = $"Retrieved {result.Items.Count} advertisements"
            });
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<AdvertisementListResponse>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving advertisements for client: {ClientId}", clientId);
            return StatusCode(500, new ApiResponse<AdvertisementListResponse>
            {
                Success = false,
                Message = "An error occurred while retrieving advertisements"
            });
        }
    }

    /// <summary>
    /// Get pending approvals (admin only)
    /// </summary>
    [HttpGet("pending-approvals")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetPendingApprovals([FromQuery] int pageNumber = 1, [FromQuery] int pageSize = 10)
    {
        try
        {
            var result = await _advertisementService.GetPendingApprovalsAsync(pageNumber, pageSize);

            return Ok(new ApiResponse<AdvertisementListResponse>
            {
                Success = true,
                Data = result,
                Message = $"Retrieved {result.Items.Count} pending advertisements"
            });
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<AdvertisementListResponse>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving pending advertisements");
            return StatusCode(500, new ApiResponse<AdvertisementListResponse>
            {
                Success = false,
                Message = "An error occurred while retrieving pending advertisements"
            });
        }
    }

    /// <summary>
    /// Create a new advertisement
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateAdvertisement([FromBody] CreateAdvertisementRequest request)
    {
        Guid clientId = Guid.Empty;
        try
        {
            clientId = GetUserIdFromToken();
            var advertisement = await _advertisementService.CreateAdvertisementAsync(clientId, request);

            return CreatedAtAction(nameof(GetAdvertisement), new { id = advertisement.Id }, new ApiResponse<AdvertisementDto>
            {
                Success = true,
                Data = advertisement,
                Message = "Advertisement created successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Resource not found: {message}", ex.Message);
            return NotFound(new ApiResponse<AdvertisementDto>
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
            return BadRequest(new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating advertisement for client: {ClientId}", clientId);
            return StatusCode(500, new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = "An error occurred while creating the advertisement"
            });
        }
    }

    /// <summary>
    /// Update an advertisement
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAdvertisement(Guid id, [FromBody] UpdateAdvertisementRequest request)
    {
        Guid clientId = Guid.Empty;
        try
        {
            clientId = GetUserIdFromToken();
            var advertisement = await _advertisementService.UpdateAdvertisementAsync(id, clientId, request);

            return Ok(new ApiResponse<AdvertisementDto>
            {
                Success = true,
                Data = advertisement,
                Message = "Advertisement updated successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {id}", id);
            return NotFound(new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (AuthorizationException ex)
        {
            _logger.LogWarning("Authorization failed for client: {ClientId}", clientId);
            return Forbid();
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating advertisement: {AdvertisementId}", id);
            return StatusCode(500, new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = "An error occurred while updating the advertisement"
            });
        }
    }

    /// <summary>
    /// Delete an advertisement
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAdvertisement(Guid id)
    {
        Guid clientId = Guid.Empty;
        try
        {
            clientId = GetUserIdFromToken();
            await _advertisementService.DeleteAdvertisementAsync(id, clientId);

            return Ok(new ApiResponse<object>
            {
                Success = true,
                Message = "Advertisement deleted successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {id}", id);
            return NotFound(new ApiResponse<object>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (AuthorizationException ex)
        {
            _logger.LogWarning("Authorization failed for client: {ClientId}", clientId);
            return Forbid();
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<object>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting advertisement: {AdvertisementId}", id);
            return StatusCode(500, new ApiResponse<object>
            {
                Success = false,
                Message = "An error occurred while deleting the advertisement"
            });
        }
    }

    /// <summary>
    /// Submit advertisement for approval
    /// </summary>
    [HttpPost("{id}/submit-approval")]
    public async Task<IActionResult> SubmitForApproval(Guid id)
    {
        Guid clientId = Guid.Empty;
        try
        {
            clientId = GetUserIdFromToken();
            var advertisement = await _advertisementService.SubmitForApprovalAsync(id, clientId);

            return Ok(new ApiResponse<AdvertisementDto>
            {
                Success = true,
                Data = advertisement,
                Message = "Advertisement submitted for approval"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {id}", id);
            return NotFound(new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (AuthorizationException ex)
        {
            _logger.LogWarning("Authorization failed for client: {ClientId}", clientId);
            return Forbid();
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting advertisement for approval: {AdvertisementId}", id);
            return StatusCode(500, new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = "An error occurred while submitting the advertisement"
            });
        }
    }

    /// <summary>
    /// Approve advertisement (admin only)
    /// </summary>
    [HttpPost("{id}/approve")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> ApproveAdvertisement(Guid id)
    {
        try
        {
            var adminName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
            var advertisement = await _advertisementService.ApproveAdvertisementAsync(id, adminName);

            return Ok(new ApiResponse<AdvertisementDto>
            {
                Success = true,
                Data = advertisement,
                Message = "Advertisement approved successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {id}", id);
            return NotFound(new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving advertisement: {AdvertisementId}", id);
            return StatusCode(500, new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = "An error occurred while approving the advertisement"
            });
        }
    }

    /// <summary>
    /// Reject advertisement (admin only)
    /// </summary>
    [HttpPost("{id}/reject")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> RejectAdvertisement(Guid id, [FromBody] ApprovalDecisionRequest request)
    {
        try
        {
            var adminName = User.FindFirst(ClaimTypes.Name)?.Value ?? "Unknown";
            var advertisement = await _advertisementService.RejectAdvertisementAsync(
                id,
                request.RejectionReason ?? "No reason provided",
                adminName);

            return Ok(new ApiResponse<AdvertisementDto>
            {
                Success = true,
                Data = advertisement,
                Message = "Advertisement rejected"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {id}", id);
            return NotFound(new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error rejecting advertisement: {AdvertisementId}", id);
            return StatusCode(500, new ApiResponse<AdvertisementDto>
            {
                Success = false,
                Message = "An error occurred while rejecting the advertisement"
            });
        }
    }

    /// <summary>
    /// Get advertisement analytics
    /// </summary>
    [HttpGet("{id}/analytics")]
    public async Task<IActionResult> GetAnalytics(Guid id)
    {
        try
        {
            var clientId = GetUserIdFromToken();
            var advertisement = await _advertisementService.GetAdvertisementAsync(id);

            if (advertisement.ClientId != clientId)
                return Forbid();

            var analytics = await _advertisementService.GetAnalyticsAsync(id);

            return Ok(new ApiResponse<AdvertisementAnalyticsDto>
            {
                Success = true,
                Data = analytics,
                Message = "Analytics retrieved successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {id}", id);
            return NotFound(new ApiResponse<AdvertisementAnalyticsDto>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving analytics for advertisement: {AdvertisementId}", id);
            return StatusCode(500, new ApiResponse<AdvertisementAnalyticsDto>
            {
                Success = false,
                Message = "An error occurred while retrieving analytics"
            });
        }
    }

    /// <summary>
    /// Upload video for advertisement
    /// </summary>
    [HttpPost("{id}/upload-video")]
    public async Task<IActionResult> UploadVideo(Guid id, IFormFile file)
    {
        Guid clientId = Guid.Empty;
        try
        {
            clientId = GetUserIdFromToken();

            if (file == null || file.Length == 0)
                return BadRequest(new ApiResponse<FileUploadResponse>
                {
                    Success = false,
                    Message = "No file provided"
                });

            using var stream = file.OpenReadStream();
            var response = await _advertisementService.UploadVideoAsync(id, clientId, stream, file.FileName);

            return Ok(new ApiResponse<FileUploadResponse>
            {
                Success = true,
                Data = response,
                Message = "Video uploaded successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {id}", id);
            return NotFound(new ApiResponse<FileUploadResponse>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (AuthorizationException ex)
        {
            _logger.LogWarning("Authorization failed for client: {ClientId}", clientId);
            return Forbid();
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<FileUploadResponse>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading video for advertisement: {AdvertisementId}", id);
            return StatusCode(500, new ApiResponse<FileUploadResponse>
            {
                Success = false,
                Message = "An error occurred while uploading the video"
            });
        }
    }

    /// <summary>
    /// Upload image for advertisement
    /// </summary>
    [HttpPost("{id}/upload-image")]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file)
    {
        Guid clientId = Guid.Empty;
        try
        {
            clientId = GetUserIdFromToken();

            if (file == null || file.Length == 0)
                return BadRequest(new ApiResponse<FileUploadResponse>
                {
                    Success = false,
                    Message = "No file provided"
                });

            using var stream = file.OpenReadStream();
            var response = await _advertisementService.UploadImageAsync(id, clientId, stream, file.FileName);

            return Ok(new ApiResponse<FileUploadResponse>
            {
                Success = true,
                Data = response,
                Message = "Image uploaded successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Advertisement not found: {id}", id);
            return NotFound(new ApiResponse<FileUploadResponse>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (AuthorizationException ex)
        {
            _logger.LogWarning("Authorization failed for client: {ClientId}", clientId);
            return Forbid();
        }
        catch (Application.Exceptions.ValidationException ex)
        {
            _logger.LogWarning("Validation error: {message}", ex.Message);
            return BadRequest(new ApiResponse<FileUploadResponse>
            {
                Success = false,
                Message = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading image for advertisement: {AdvertisementId}", id);
            return StatusCode(500, new ApiResponse<FileUploadResponse>
            {
                Success = false,
                Message = "An error occurred while uploading the image"
            });
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
