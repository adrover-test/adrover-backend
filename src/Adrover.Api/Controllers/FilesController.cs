using Adrover.Application.DTOs;
using Adrover.Infrastructure.Services;
using Adrover.Application.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Adrover.Api.Controllers;

/// <summary>
/// File management endpoints for uploading and downloading files
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly IFileStorageService _fileStorageService;
    private readonly ILogger<FilesController> _logger;
    
    private const long MaxFileSize = 100 * 1024 * 1024; // 100 MB
    private const long MaxVideoSize = 500 * 1024 * 1024; // 500 MB
    private readonly string[] _allowedImageTypes = { "image/jpeg", "image/png", "image/gif" };
    private readonly string[] _allowedVideoTypes = { "video/mp4", "video/quicktime", "video/mpeg" };

    public FilesController(IFileStorageService fileStorageService, ILogger<FilesController> logger)
    {
        _fileStorageService = fileStorageService;
        _logger = logger;
    }

    /// <summary>
    /// Upload advertisement video file
    /// </summary>
    [HttpPost("upload-video")]
    public async Task<IActionResult> UploadVideo(IFormFile file)
    {
        try
        {
            var userId = GetUserIdFromToken();

            if (file == null || file.Length == 0)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "EMPTY_FILE",
                        Message = "File is required"
                    }
                });
            }

            if (file.Length > MaxVideoSize)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "FILE_TOO_LARGE",
                        Message = $"Video file size must not exceed {MaxVideoSize / (1024 * 1024)} MB"
                    }
                });
            }

            if (!_allowedVideoTypes.Contains(file.ContentType))
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_FILE_TYPE",
                        Message = "Only MP4, MOV, and MPEG video formats are allowed"
                    }
                });
            }

            var fileName = $"ads/{userId}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            
            using (var stream = file.OpenReadStream())
            {
                var url = await _fileStorageService.UploadFileAsync("advertisements", fileName, stream);
                
                _logger.LogInformation("Video uploaded successfully for user {UserId}: {FileName}", userId, fileName);
                
                return Ok(new ApiResponse<FileUploadResponse>
                {
                    Success = true,
                    Data = new FileUploadResponse
                    {
                        Url = url,
                        FileSize = file.Length,
                        UploadedAt = DateTime.UtcNow
                    },
                    Message = "Video uploaded successfully"
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Azure Blob Storage not configured");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "STORAGE_ERROR",
                    Message = "File storage is not properly configured"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading video");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error uploading video"
                }
            });
        }
    }

    /// <summary>
    /// Upload advertisement image file
    /// </summary>
    [HttpPost("upload-image")]
    public async Task<IActionResult> UploadImage(IFormFile file)
    {
        try
        {
            var userId = GetUserIdFromToken();

            if (file == null || file.Length == 0)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "EMPTY_FILE",
                        Message = "File is required"
                    }
                });
            }

            if (file.Length > MaxFileSize)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "FILE_TOO_LARGE",
                        Message = $"Image file size must not exceed {MaxFileSize / (1024 * 1024)} MB"
                    }
                });
            }

            if (!_allowedImageTypes.Contains(file.ContentType))
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_FILE_TYPE",
                        Message = "Only JPEG, PNG, and GIF image formats are allowed"
                    }
                });
            }

            var fileName = $"ads/{userId}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            
            using (var stream = file.OpenReadStream())
            {
                var url = await _fileStorageService.UploadFileAsync("advertisements", fileName, stream);
                
                _logger.LogInformation("Image uploaded successfully for user {UserId}: {FileName}", userId, fileName);
                
                return Ok(new ApiResponse<FileUploadResponse>
                {
                    Success = true,
                    Data = new FileUploadResponse
                    {
                        Url = url,
                        FileSize = file.Length,
                        UploadedAt = DateTime.UtcNow
                    },
                    Message = "Image uploaded successfully"
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Azure Blob Storage not configured");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "STORAGE_ERROR",
                    Message = "File storage is not properly configured"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading image");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error uploading image"
                }
            });
        }
    }

    /// <summary>
    /// Upload user profile image
    /// </summary>
    [HttpPost("upload-profile-image")]
    public async Task<IActionResult> UploadProfileImage(IFormFile file)
    {
        try
        {
            var userId = GetUserIdFromToken();

            if (file == null || file.Length == 0)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "EMPTY_FILE",
                        Message = "File is required"
                    }
                });
            }

            if (file.Length > MaxFileSize)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "FILE_TOO_LARGE",
                        Message = $"Profile image size must not exceed {MaxFileSize / (1024 * 1024)} MB"
                    }
                });
            }

            if (!_allowedImageTypes.Contains(file.ContentType))
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_FILE_TYPE",
                        Message = "Only JPEG, PNG, and GIF image formats are allowed"
                    }
                });
            }

            var fileName = $"profiles/{userId}/{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            
            using (var stream = file.OpenReadStream())
            {
                var url = await _fileStorageService.UploadFileAsync("profiles", fileName, stream);
                
                _logger.LogInformation("Profile image uploaded successfully for user {UserId}: {FileName}", userId, fileName);
                
                return Ok(new ApiResponse<FileUploadResponse>
                {
                    Success = true,
                    Data = new FileUploadResponse
                    {
                        Url = url,
                        FileSize = file.Length,
                        UploadedAt = DateTime.UtcNow
                    },
                    Message = "Profile image uploaded successfully"
                });
            }
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Azure Blob Storage not configured");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "STORAGE_ERROR",
                    Message = "File storage is not properly configured"
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading profile image");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error uploading profile image"
                }
            });
        }
    }

    /// <summary>
    /// Download a file
    /// </summary>
    [HttpGet("download/{containerName}/{*filePath}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadFile(string containerName, string filePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(containerName) || string.IsNullOrWhiteSpace(filePath))
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_PATH",
                        Message = "Container name and file path are required"
                    }
                });
            }

            // Allow only certain containers
            var allowedContainers = new[] { "advertisements", "profiles" };
            if (!allowedContainers.Contains(containerName))
            {
                return Forbid();
            }

            var stream = await _fileStorageService.DownloadFileAsync(containerName, filePath);
            var contentType = GetContentType(filePath);
            
            _logger.LogInformation("File downloaded: {ContainerName}/{FilePath}", containerName, filePath);
            
            return File(stream, contentType, Path.GetFileName(filePath));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error downloading file");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error downloading file"
                }
            });
        }
    }

    /// <summary>
    /// Get a SAS URL for temporary file access
    /// </summary>
    [HttpGet("sas-url/{containerName}/{*filePath}")]
    public async Task<IActionResult> GetSasUrl(string containerName, string filePath, [FromQuery] int expirationMinutes = 60)
    {
        try
        {
            var userId = GetUserIdFromToken();

            if (string.IsNullOrWhiteSpace(containerName) || string.IsNullOrWhiteSpace(filePath))
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_PATH",
                        Message = "Container name and file path are required"
                    }
                });
            }

            // Allow only certain containers
            var allowedContainers = new[] { "advertisements", "profiles" };
            if (!allowedContainers.Contains(containerName))
            {
                return Forbid();
            }

            if (expirationMinutes <= 0 || expirationMinutes > 1440) // Max 24 hours
            {
                expirationMinutes = 60;
            }

            var sasUrl = await _fileStorageService.GenerateSasUrlAsync(
                containerName, 
                filePath, 
                TimeSpan.FromMinutes(expirationMinutes));
            
            _logger.LogInformation("SAS URL generated for user {UserId}: {ContainerName}/{FilePath}", 
                userId, containerName, filePath);
            
            return Ok(new ApiResponse<object>
            {
                Success = true,
                Data = new { url = sasUrl, expiresIn = expirationMinutes },
                Message = "SAS URL generated successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating SAS URL");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error generating SAS URL"
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

    /// <summary>
    /// Helper method to determine content type based on file extension
    /// </summary>
    private string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".mpeg" => "video/mpeg",
            _ => "application/octet-stream"
        };
    }
}
