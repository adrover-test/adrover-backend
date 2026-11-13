using Adrover.Application.DTOs;
using Adrover.Application.Services;
using Adrover.Application.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Adrover.Api.Controllers;

/// <summary>
/// Wallet management endpoints
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WalletsController : ControllerBase
{
    private readonly IWalletService _walletService;
    private readonly ILogger<WalletsController> _logger;

    public WalletsController(IWalletService walletService, ILogger<WalletsController> logger)
    {
        _walletService = walletService;
        _logger = logger;
    }

    /// <summary>
    /// Get wallet balance for current user
    /// </summary>
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance()
    {
        try
        {
            var userId = GetUserIdFromToken();
            var wallet = await _walletService.GetWalletAsync(userId);
            
            _logger.LogInformation("Retrieved wallet balance for user {UserId}", userId);
            
            return Ok(new ApiResponse<WalletDto>
            {
                Success = true,
                Data = wallet,
                Message = "Wallet balance retrieved successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Wallet not found: {Message}", ex.Message);
            return NotFound(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "WALLET_NOT_FOUND",
                    Message = ex.Message
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving wallet balance");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving wallet balance"
                }
            });
        }
    }

    /// <summary>
    /// Topup wallet with amount
    /// </summary>
    [HttpPost("topup")]
    public async Task<IActionResult> TopupWallet([FromBody] WalletTopupRequest request)
    {
        try
        {
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

            var userId = GetUserIdFromToken();
            var result = await _walletService.TopupWalletAsync(userId, request);
            
            _logger.LogInformation("Wallet topup successful for user {UserId}: {Amount} INR", userId, request.Amount);
            
            return Ok(new ApiResponse<WalletTopupResponse>
            {
                Success = true,
                Data = result,
                Message = "Wallet topup successful"
            });
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation error during topup: {Message}", ex.Message);
            return BadRequest(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "VALIDATION_ERROR",
                    Message = ex.Message
                }
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Resource not found: {Message}", ex.Message);
            return NotFound(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "NOT_FOUND",
                    Message = ex.Message
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during wallet topup");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error processing wallet topup"
                }
            });
        }
    }

    /// <summary>
    /// Get wallet transaction history with pagination
    /// </summary>
    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactionHistory([FromQuery] int skip = 0, [FromQuery] int take = 10)
    {
        try
        {
            var userId = GetUserIdFromToken();
            var history = await _walletService.GetTransactionHistoryAsync(userId, skip, take);
            
            _logger.LogInformation("Retrieved transaction history for user {UserId}", userId);
            
            return Ok(new ApiResponse<WalletTransactionHistoryResponse>
            {
                Success = true,
                Data = history,
                Message = "Transaction history retrieved successfully"
            });
        }
        catch (NotFoundException ex)
        {
            _logger.LogWarning("Wallet not found: {Message}", ex.Message);
            return NotFound(new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "WALLET_NOT_FOUND",
                    Message = ex.Message
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving transaction history");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving transaction history"
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
