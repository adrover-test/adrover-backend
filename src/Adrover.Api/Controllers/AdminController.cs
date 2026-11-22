using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Advertisement;
using Adrover.Application.Services;
using Adrover.Application.Exceptions;
using Adrover.Data.Context;
using Adrover.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    private readonly AdRoverDbContext _dbContext;
    private readonly ILogger<AdminController> _logger;

    public AdminController(
        IAdvertisementService advertisementService,
        IPaymentService paymentService,
        IUserService userService,
        AdRoverDbContext dbContext,
        ILogger<AdminController> logger)
    {
        _advertisementService = advertisementService;
        _paymentService = paymentService;
        _userService = userService;
        _dbContext = dbContext;
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
    /// Get operations overview charts with selectable range (daily, weekly, monthly, yearly)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("dashboard/operations-overview")]
    public async Task<IActionResult> GetOperationsOverview([FromQuery] string? range, CancellationToken cancellationToken)
    {
        try
        {
            var rangeKey = string.IsNullOrWhiteSpace(range) ? "monthly" : range.Trim().ToLowerInvariant();
            if (rangeKey is not ("daily" or "weekly" or "monthly" or "yearly"))
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_RANGE",
                        Message = "Use range=daily, weekly, monthly, or yearly."
                    }
                });
            }

            var now = DateTime.UtcNow;
            var (rangeStartUtc, rangeEndUtc, bucketCount) = ResolveRangeWindow(now, rangeKey);

            var adPlayRows = await _dbContext.AdPlays.AsNoTracking()
                .Where(adPlay => adPlay.PlayedAt >= rangeStartUtc && adPlay.PlayedAt < rangeEndUtc)
                .Select(adPlay => new { adPlay.PlayedAt, adPlay.TotalPlays })
                .ToListAsync(cancellationToken);

            var paymentRows = await _dbContext.Payments.AsNoTracking()
                .Where(payment => payment.CreatedAt >= rangeStartUtc
                    && payment.CreatedAt < rangeEndUtc
                    && (payment.Status == PaymentStatus.Completed || payment.Status == PaymentStatus.Refunded))
                .Select(payment => new
                {
                    payment.CreatedAt,
                    payment.Status,
                    payment.Amount
                })
                .ToListAsync(cancellationToken);

            var statusGroupedRaw = await _dbContext.Advertisements.AsNoTracking()
                .GroupBy(advertisement => advertisement.Status)
                .Select(group => new
                {
                    Status = group.Key,
                    Count = group.Count()
                })
                .ToListAsync(cancellationToken);

            var statusGrouped = statusGroupedRaw
                .Select(item => new AdminAdvertisementStatusPointDto
                {
                    Status = item.Status switch
                    {
                        AdvertisementStatus.PendingApproval => "Pending approval",
                        AdvertisementStatus.Active => "Active",
                        AdvertisementStatus.Approved => "Approved",
                        AdvertisementStatus.Completed => "Completed",
                        AdvertisementStatus.Rejected => "Rejected",
                        AdvertisementStatus.Cancelled => "Cancelled",
                        AdvertisementStatus.Draft => "Draft",
                        _ => item.Status.ToString()
                    },
                    Count = item.Count
                })
                .OrderByDescending(item => item.Count)
                .ToList();

            var adPlayLookup = adPlayRows
                .GroupBy(item => GetBucketStart(item.PlayedAt, rangeKey))
                .ToDictionary(group => group.Key, group => group.Sum(item => (long)item.TotalPlays));

            var revenueLookup = paymentRows
                .GroupBy(item => GetBucketStart(item.CreatedAt, rangeKey))
                .ToDictionary(group => group.Key, group => group.Sum(item => item.Status == PaymentStatus.Refunded ? -item.Amount : item.Amount));

            var buckets = Enumerable.Range(0, bucketCount)
                .Select(index => AddBucket(rangeStartUtc, index, rangeKey))
                .ToList();

            var monthlyAdPlays = buckets
                .Select(bucket => new AdminMonthlyAdPlaysPointDto
                {
                    Month = FormatBucketLabel(bucket, rangeKey),
                    TotalPlays = adPlayLookup.GetValueOrDefault(bucket, 0L)
                })
                .ToList();

            var monthlyRevenue = buckets
                .Select(bucket => new AdminMonthlyRevenuePointDto
                {
                    Month = FormatBucketLabel(bucket, rangeKey),
                    NetRevenue = revenueLookup.GetValueOrDefault(bucket, 0m)
                })
                .ToList();

            var overview = new AdminOperationsOverviewDto
            {
                Currency = "INR",
                Range = rangeKey,
                StartUtc = rangeStartUtc,
                EndUtc = rangeEndUtc,
                TotalAdPlaysInPeriod = monthlyAdPlays.Sum(item => item.TotalPlays),
                NetRevenueInPeriod = monthlyRevenue.Sum(item => item.NetRevenue),
                TotalAds = await _dbContext.Advertisements.AsNoTracking().CountAsync(cancellationToken),
                MonthlyAdPlays = monthlyAdPlays,
                MonthlyCampaignRevenue = monthlyRevenue,
                AdvertisementsByStatus = statusGrouped
            };

            return Ok(new ApiResponse<AdminOperationsOverviewDto>
            {
                Success = true,
                Data = overview,
                Message = "Operations overview retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving operations overview");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving operations overview"
                }
            });
        }
    }

    /// <summary>
    /// Get admin dashboard summary cards
    /// </summary>
    [AllowAnonymous]
    [HttpGet("dashboard/summary")]
    public async Task<IActionResult> GetDashboardSummary(CancellationToken cancellationToken)
    {
        try
        {
            var totalCollected = await _dbContext.Payments.AsNoTracking()
                .Where(payment => payment.Status == PaymentStatus.Completed)
                .Select(payment => (decimal?)payment.Amount)
                .SumAsync(cancellationToken) ?? 0m;
            var totalRefunded = await _dbContext.Payments.AsNoTracking()
                .Where(payment => payment.Status == PaymentStatus.Refunded)
                .Select(payment => (decimal?)payment.Amount)
                .SumAsync(cancellationToken) ?? 0m;

            var summary = new AdminDashboardSummaryDto
            {
                ClientsPresent = await _dbContext.Users.AsNoTracking()
                    .CountAsync(user => user.Role == UserRole.Client, cancellationToken),
                Enquiries = await _dbContext.Enquiries.AsNoTracking().CountAsync(cancellationToken),
                CabsInDirectory = await _dbContext.Users.AsNoTracking()
                    .CountAsync(user => user.Role == UserRole.Driver && user.IsActive, cancellationToken),
                AdsAwaitingApproval = await _dbContext.Advertisements.AsNoTracking()
                    .CountAsync(advertisement => advertisement.Status == AdvertisementStatus.PendingApproval, cancellationToken),
                TotalAds = await _dbContext.Advertisements.AsNoTracking().CountAsync(cancellationToken),
                TotalAdPlays = await _dbContext.AdPlays.AsNoTracking()
                    .Select(adPlay => (long?)adPlay.TotalPlays)
                    .SumAsync(cancellationToken) ?? 0L,
                TotalRevenueTillDate = totalCollected - totalRefunded
            };

            _logger.LogInformation("Dashboard summary retrieved");

            return Ok(new ApiResponse<AdminDashboardSummaryDto>
            {
                Success = true,
                Data = summary,
                Message = "Dashboard summary retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving dashboard summary");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving dashboard summary"
                }
            });
        }
    }

    /// <summary>
    /// Get clients list for admin clients page
    /// </summary>
    [AllowAnonymous]
    [HttpGet("clients")]
    public async Task<IActionResult> GetClients(
        [FromQuery] string? search,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (pageNumber < 1 || pageSize < 1 || pageSize > 100 || (long)(pageNumber - 1) * pageSize > int.MaxValue)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_REQUEST",
                        Message = "Choose a valid page number and page size (1-100)."
                    }
                });
            }

            var query = _dbContext.Users.AsNoTracking()
                .Where(user => user.Role == UserRole.Client);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(user =>
                    user.FullName.Contains(term)
                    || user.Email.Contains(term)
                    || user.Phone.Contains(term));
            }

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(user => user.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(user => new AdminClientListItemDto
                {
                    ClientId = user.Id,
                    ClientOrBusiness = user.FullName,
                    ContactName = user.FullName,
                    Email = user.Email,
                    Phone = string.IsNullOrWhiteSpace(user.Phone) ? "Not provided" : user.Phone,
                    Advertisements = _dbContext.Advertisements.Count(advertisement => advertisement.ClientId == user.Id)
                })
                .ToListAsync(cancellationToken);

            return Ok(new ApiResponse<AdminClientListResponseDto>
            {
                Success = true,
                Data = new AdminClientListResponseDto
                {
                    Items = items,
                    Total = total,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                },
                Message = "Clients retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving clients list");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving clients list"
                }
            });
        }
    }

    /// <summary>
    /// Get enquiries list for admin enquiries page
    /// </summary>
    [AllowAnonymous]
    [HttpGet("enquiries")]
    public async Task<IActionResult> GetEnquiries(
        [FromQuery] string? search,
        [FromQuery] string? status,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (pageNumber < 1 || pageSize < 1 || pageSize > 100 || (long)(pageNumber - 1) * pageSize > int.MaxValue)
            {
                return BadRequest(new ApiErrorResponse
                {
                    Error = new ErrorDetails
                    {
                        Code = "INVALID_REQUEST",
                        Message = "Choose a valid page number and page size (1-100)."
                    }
                });
            }

            var allEnquiries = _dbContext.Enquiries.AsNoTracking();
            var summary = new AdminEnquirySummaryDto
            {
                TotalEnquiries = await allEnquiries.CountAsync(cancellationToken),
                AwaitingReply = await allEnquiries.CountAsync(enquiry => enquiry.Status != "Resolved", cancellationToken),
                Replied = await allEnquiries.CountAsync(enquiry => enquiry.Status == "Resolved", cancellationToken)
            };

            var query = allEnquiries;
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(enquiry =>
                    enquiry.Name.Contains(term)
                    || enquiry.Email.Contains(term)
                    || enquiry.Phone.Contains(term)
                    || enquiry.Subject.Contains(term)
                    || enquiry.Message.Contains(term));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = status.Trim().ToLowerInvariant() switch
                {
                    "awaiting" or "awaiting-reply" or "awaiting reply" => query.Where(enquiry => enquiry.Status != "Resolved"),
                    "replied" => query.Where(enquiry => enquiry.Status == "Resolved"),
                    _ => query
                };
            }

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(enquiry => enquiry.CreatedAt)
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .Select(enquiry => new AdminEnquiryListItemDto
                {
                    EnquiryId = enquiry.Id,
                    BusinessOrName = ExtractFromMessage(enquiry.Message, "Business") ?? enquiry.Name,
                    ContactName = enquiry.Name,
                    ReceivedAt = enquiry.CreatedAt,
                    Email = enquiry.Email,
                    Phone = string.IsNullOrWhiteSpace(enquiry.Phone) ? "Not provided" : enquiry.Phone,
                    Objective = enquiry.Subject,
                    Requirements = ExtractFromMessage(enquiry.Message, "Additional details") ?? enquiry.Message,
                    ReplyStatus = enquiry.Status == "Resolved" ? "Replied" : "Awaiting reply",
                    InternalNotes = string.Empty
                })
                .ToListAsync(cancellationToken);

            return Ok(new ApiResponse<AdminEnquiryListResponseDto>
            {
                Success = true,
                Data = new AdminEnquiryListResponseDto
                {
                    Summary = summary,
                    Items = items,
                    Total = total,
                    PageNumber = pageNumber,
                    PageSize = pageSize
                },
                Message = "Enquiries retrieved successfully"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving enquiries list");
            return StatusCode(500, new ApiErrorResponse
            {
                Error = new ErrorDetails
                {
                    Code = "INTERNAL_ERROR",
                    Message = "Error retrieving enquiries list"
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

    private static (DateTime RangeStartUtc, DateTime RangeEndUtc, int BucketCount) ResolveRangeWindow(DateTime nowUtc, string range)
    {
        return range switch
        {
            "daily" => (nowUtc.Date.AddDays(-13), nowUtc.Date.AddDays(1), 14),
            "weekly" => (StartOfWeek(nowUtc.Date).AddDays(-77), StartOfWeek(nowUtc.Date).AddDays(7), 12),
            "yearly" => (new DateTime(nowUtc.Year - 4, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(nowUtc.Year + 1, 1, 1, 0, 0, 0, DateTimeKind.Utc), 5),
            _ => (new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(-11),
                new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1), 12)
        };
    }

    private static DateTime StartOfWeek(DateTime value)
    {
        var offset = (7 + (int)value.DayOfWeek - (int)DayOfWeek.Monday) % 7;
        return value.AddDays(-offset);
    }

    private static DateTime GetBucketStart(DateTime value, string range)
    {
        var utc = DateTime.SpecifyKind(value, DateTimeKind.Utc);
        return range switch
        {
            "daily" => utc.Date,
            "weekly" => StartOfWeek(utc.Date),
            "yearly" => new DateTime(utc.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            _ => new DateTime(utc.Year, utc.Month, 1, 0, 0, 0, DateTimeKind.Utc)
        };
    }

    private static DateTime AddBucket(DateTime start, int offset, string range)
    {
        return range switch
        {
            "daily" => start.AddDays(offset),
            "weekly" => start.AddDays(offset * 7),
            "yearly" => start.AddYears(offset),
            _ => start.AddMonths(offset)
        };
    }

    private static string FormatBucketLabel(DateTime bucketStart, string range)
    {
        return range switch
        {
            "daily" => bucketStart.ToString("dd MMM"),
            "weekly" => $"Week of {bucketStart:dd MMM}",
            "yearly" => bucketStart.ToString("yyyy"),
            _ => bucketStart.ToString("MMM yy")
        };
    }

    private static string? ExtractFromMessage(string message, string key)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return null;
        }

        var marker = $"{key}:";
        var lines = message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        foreach (var line in lines)
        {
            if (line.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
            {
                var value = line[marker.Length..].Trim();
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
        }

        return null;
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
