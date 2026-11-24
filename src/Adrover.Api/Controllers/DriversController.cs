using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Adrover.Application.DTOs;
using Adrover.Application.DTOs.Driver;
using Adrover.Data.Context;
using Adrover.Domain.Entities;
using Adrover.Domain.Exceptions;
using Adrover.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Adrover.Api.Controllers;

[ApiController]
[Route("api/v1/drivers")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed partial class DriversController(
    AdRoverDbContext db,
    IPasswordService passwords,
    ITokenService tokens,
    IFileStorageService files,
    IConfiguration configuration,
    ILogger<DriversController> logger) : ControllerBase
{
    [HttpPost("auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting("driver-login")]
    [RequestSizeLimit(2048)]
    public async Task<IActionResult> Login(DriverLoginRequest request, CancellationToken cancellationToken)
    {
        var phone = NormalizePhone(request.Phone);
        if (phone == null || Encoding.UTF8.GetByteCount(request.Password) > 72)
            throw new AuthenticationException("Invalid mobile number or password");

        var driver = await db.Drivers.SingleOrDefaultAsync(d => d.Phone == phone, cancellationToken);
        if (driver == null)
        {
            passwords.HashPassword(request.Password);
            throw new AuthenticationException("Invalid mobile number or password");
        }

        if (!passwords.VerifyPassword(request.Password, driver.PasswordHash) || !driver.IsActive)
            throw new AuthenticationException("Invalid mobile number or password");

        driver.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        logger.LogInformation("Driver logged in: {DriverId}", driver.Id);
        return Ok(new ApiResponse<object>
        {
            Success = true,
            Data = new
            {
                accessToken = tokens.GenerateDriverAccessToken(driver),
                expiresIn = (configuration.GetValue<int?>("Jwt:ExpirationMinutes") ?? 15) * 60,
                tokenType = "Bearer"
            },
            Message = "Login successful"
        });
    }

    [HttpGet("profile")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetProfile(CancellationToken cancellationToken)
    {
        if (User.FindFirst("driver_account")?.Value != "true" ||
            !Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id))
            return Forbid();

        var driver = await db.Drivers.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == id && d.IsActive, cancellationToken);
        if (driver == null) return Unauthorized();
        Response.Headers.CacheControl = "no-store";
        return await BuildProfileAsync(driver, cancellationToken);
    }

    [HttpGet]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> GetDrivers(CancellationToken cancellationToken)
    {
        var drivers = await db.Drivers.AsNoTracking()
            .OrderBy(d => d.FullName)
            .Select(d => new DriverDto
            {
                Id = d.Id,
                FullName = d.FullName,
                Phone = d.Phone,
                Email = d.Email,
                VehicleNumber = d.VehicleNumber,
                VehicleModel = d.VehicleModel,
                ServiceArea = d.ServiceArea,
                ScreenId = d.ScreenId,
                Status = d.Status,
                IsActive = d.IsActive,
                CreatedAt = d.CreatedAt,
                UpdatedAt = d.UpdatedAt,
                LastLoginAt = d.LastLoginAt
            })
            .ToListAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(new ApiResponse<List<DriverDto>> { Success = true, Data = drivers });
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> CreateDriver(CreateDriverRequest request, CancellationToken cancellationToken)
    {
        ValidatePassword(request.Password);
        var driver = new Driver
        {
            Id = Guid.NewGuid(),
            CreatedAt = DateTime.UtcNow,
            CreatedBy = User.Identity?.Name,
            IsActive = true
        };
        await ApplyDetailsAsync(driver, request, cancellationToken);
        driver.PasswordHash = passwords.HashPassword(request.Password);
        db.Drivers.Add(driver);
        await SaveChangesAsync(cancellationToken);
        logger.LogInformation("Driver created: {DriverId}", driver.Id);
        Response.Headers.CacheControl = "no-store";
        return Ok(new ApiResponse<DriverDto>
        {
            Success = true,
            Data = MapDriver(driver),
            Message = "Driver created successfully"
        });
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin")]
    [RequestSizeLimit(8192)]
    public async Task<IActionResult> UpdateDriver(Guid id, UpdateDriverRequest request, CancellationToken cancellationToken)
    {
        var driver = await db.Drivers.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (driver == null) return NotFound(new { message = "Driver not found." });
        if (request.Password != null) ValidatePassword(request.Password);
        await ApplyDetailsAsync(driver, request, cancellationToken);
        if (request.Password != null) driver.PasswordHash = passwords.HashPassword(request.Password);
        driver.UpdatedAt = DateTime.UtcNow;
        await SaveChangesAsync(cancellationToken);
        Response.Headers.CacheControl = "no-store";
        return Ok(new ApiResponse<DriverDto>
        {
            Success = true,
            Data = MapDriver(driver),
            Message = "Driver updated successfully"
        });
    }

    [HttpPut("profile")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> UpdateProfile([FromBody] DriverProfileUpdateRequest request, CancellationToken cancellationToken)
    {
        if (User.FindFirst("driver_account")?.Value != "true" ||
            !Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id))
            return Forbid();

        var driver = await db.Drivers.SingleOrDefaultAsync(d => d.Id == id && d.IsActive, cancellationToken);
        if (driver == null) return Unauthorized();

        var name = request.Name.Trim();
        var email = request.Email?.Trim().ToLowerInvariant();
        if (name.Length == 0)
            throw new BusinessLogicException("Enter a valid driver name.");
        if (email != null && !Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            throw new BusinessLogicException("Enter a valid driver email address.");

        driver.FullName = name;
        driver.Email = email ?? string.Empty;
        driver.Address = string.IsNullOrWhiteSpace(request.Address) ? null : request.Address.Trim();
        driver.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return await BuildProfileAsync(driver, cancellationToken);
    }

    [HttpPut("language")]
    [Authorize(Roles = "Driver")]
    [RequestSizeLimit(512)]
    public async Task<IActionResult> SaveLanguage(DriverLanguageRequest request, CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        db.Attach(driver);
        driver.PreferredLanguage = request.PreferredLanguage;
        driver.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("profile/photo")]
    [Authorize(Roles = "Driver")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> UploadPhoto(IFormFile photo, CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        var blob = await UploadImageAsync(photo, $"photos/{driver.Id}", cancellationToken);
        db.Attach(driver);
        driver.PhotoBlobName = blob;
        driver.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { photoUrl = await ImageUrlAsync(blob) });
    }

    [HttpGet("vehicle")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetVehicle(CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        return Ok(await BuildVehicleAsync(driver, cancellationToken));
    }

    [HttpGet("dashboard")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetDashboard(CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();

        return Ok(await BuildDashboardAsync(driver, cancellationToken));
    }

    [HttpGet("advertisements/today")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetTodayAdvertisements(CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        return Ok(new { items = await TodayAdsAsync(driver.Id, cancellationToken) });
    }

    [HttpGet("advertisements/history")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetPlaybackHistory([FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        var start = ParseDate(from, DateTime.UtcNow.Date.AddDays(-13));
        var end = ParseDate(to, DateTime.UtcNow.Date).AddDays(1);
        if (end <= start || (end - start).TotalDays > 366)
            throw new BusinessLogicException("Choose a date range of at most 366 days.");
        var items = await db.DriverPlaybacks.AsNoTracking()
            .Where(p => p.Assignment.DriverId == driver.Id && p.PlayedAt >= start && p.PlayedAt < end)
            .GroupBy(p => new { p.Assignment.AdvertisementId, Date = p.PlayedAt.Date, p.Assignment.Advertisement.Name, p.Assignment.Advertisement.Brand })
            .Select(g => new
            {
                id = g.Key.AdvertisementId,
                date = g.Key.Date,
                adName = g.Key.Name,
                advertiserName = g.Key.Brand,
                plays = g.Sum(p => p.Plays)
            })
            .OrderByDescending(x => x.date)
            .ToListAsync(cancellationToken);
        return Ok(new { items = items.Select(x => new
        {
            id = $"{x.id:N}-{x.date:yyyyMMdd}",
            date = x.date.ToString("yyyy-MM-dd"),
            x.adName,
            x.advertiserName,
            plays = x.plays,
            status = "completed"
        }) });
    }

    [HttpGet("earnings")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetEarnings([FromQuery] string period = "monthly", CancellationToken cancellationToken = default)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        return Ok(await BuildEarningsAsync(driver.Id, period, cancellationToken));
    }

    [HttpGet("payments")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetPayments([FromQuery] string period = "monthly", CancellationToken cancellationToken = default)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        var start = PeriodStart(period);
        return Ok(new { items = await db.DriverPayouts.AsNoTracking()
            .Where(p => p.DriverId == driver.Id && p.PeriodEnd >= start)
            .OrderByDescending(p => p.CreatedAt).ToListAsync(cancellationToken) });
    }

    [HttpGet("payments/{id:guid}")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetPayment(Guid id, CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        var payment = await db.DriverPayouts.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id && p.DriverId == driver.Id, cancellationToken);
        return payment == null ? NotFound(new { message = "Payment not found." }) : Ok(payment);
    }

    [HttpGet("notifications")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetNotifications(CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        var query = db.DriverNotifications.AsNoTracking().Where(n => n.DriverId == driver.Id);
        return Ok(new { items = await query.OrderByDescending(n => n.CreatedAt).ToListAsync(cancellationToken),
            unreadCount = await query.CountAsync(n => !n.IsRead, cancellationToken) });
    }

    [HttpPut("notifications/{id:guid}/read")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> MarkNotificationRead(Guid id, CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        var notification = await db.DriverNotifications.SingleOrDefaultAsync(n => n.Id == id && n.DriverId == driver.Id, cancellationToken);
        if (notification == null) return NotFound(new { message = "Notification not found." });
        notification.IsRead = true;
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpGet("issues")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetIssues(CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        var issues = await db.DriverIssues.AsNoTracking().Where(i => i.DriverId == driver.Id)
            .OrderByDescending(i => i.UpdatedAt).ToListAsync(cancellationToken);
        return Ok(new { items = issues.Select(IssueSummary) });
    }

    [HttpGet("issues/{id:guid}")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> GetIssue(Guid id, CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        var issue = await db.DriverIssues.AsNoTracking().Include(i => i.History)
            .SingleOrDefaultAsync(i => i.Id == id && i.DriverId == driver.Id, cancellationToken);
        return issue == null ? NotFound(new { message = "Issue not found." }) : Ok(await IssueDetailAsync(issue, cancellationToken));
    }

    [HttpPost("issues")]
    [Authorize(Roles = "Driver")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> SubmitIssue([FromForm] DriverIssueRequest request, IFormFile? attachment, CancellationToken cancellationToken)
    {
        var driver = await FindDriverAsync(cancellationToken);
        if (driver == null) return Unauthorized();
        return await CreateIssueAsync(driver, request, attachment, cancellationToken);
    }

    [HttpPost("issues/{id:guid}/confirm")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> ConfirmIssue(Guid id, CancellationToken cancellationToken)
        => await ChangeIssueAsync(id, null, cancellationToken);

    [HttpPost("issues/{id:guid}/reopen")]
    [Authorize(Roles = "Driver")]
    public async Task<IActionResult> ReopenIssue(Guid id, DriverIssueReopenRequest request, CancellationToken cancellationToken)
        => await ChangeIssueAsync(id, request.Reason, cancellationToken);

    [HttpPost("{id:guid}/password")]
    [Authorize(Roles = "SuperAdmin")]
    [RequestSizeLimit(2048)]
    public async Task<IActionResult> SetPassword(Guid id, SetDriverPasswordRequest request, CancellationToken cancellationToken)
    {
        var driver = await db.Drivers.SingleOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (driver == null) return NotFound(new { message = "Driver not found." });
        ValidatePassword(request.Password);
        driver.PasswordHash = passwords.HashPassword(request.Password);
        driver.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Driver password regenerated: {DriverId}", driver.Id);
        Response.Headers.CacheControl = "no-store";
        return Ok(new ApiResponse<object> { Success = true, Message = "Driver password updated successfully" });
    }

    private async Task<Driver?> FindDriverAsync(CancellationToken cancellationToken)
    {
        if (User.FindFirst("driver_account")?.Value != "true" ||
            !Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id))
            return null;
        return await db.Drivers.AsNoTracking()
            .SingleOrDefaultAsync(d => d.Id == id && d.IsActive, cancellationToken);
    }

    private async Task<IActionResult> BuildProfileAsync(Driver driver, CancellationToken cancellationToken)
    {
        Response.Headers.CacheControl = "no-store";
        return Ok(new
        {
            id = driver.Id.ToString(),
            name = driver.FullName,
            mobile = driver.Phone,
            email = driver.Email,
            address = driver.Address,
            photoUrl = await ImageUrlAsync(driver.PhotoBlobName),
            licenceNumber = (string?)null,
            licenceExpiry = (string?)null,
            accountStatus = "active",
            registeredAt = driver.CreatedAt,
            preferredLanguage = driver.PreferredLanguage,
            vehicle = await BuildVehicleAsync(driver, cancellationToken),
            documents = Array.Empty<object>()
        });
    }

    private async Task ApplyDetailsAsync(Driver driver, DriverDetailsRequest request, CancellationToken cancellationToken)
    {
        var name = request.FullName.Trim();
        var model = request.VehicleModel.Trim();
        var area = request.ServiceArea.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var vehicle = Regex.Replace(request.VehicleNumber.Trim().ToUpperInvariant(), @"[\s-]", "");
        var screen = request.ScreenId.Trim().ToUpperInvariant();
        var phone = NormalizePhone(request.Phone);
        if (name.Length == 0 || model.Length == 0 || area.Length == 0)
            throw new BusinessLogicException("Enter a driver name, vehicle model and service area.");
        if (phone == null)
            throw new BusinessLogicException("Enter a valid 10-digit Indian mobile number.");
        if (!Regex.IsMatch(email, @"^[^\s@]+@[^\s@]+\.[^\s@]+$"))
            throw new BusinessLogicException("Enter a valid driver email address.");
        if (!Regex.IsMatch(vehicle, "^[A-Z0-9]{6,15}$"))
            throw new BusinessLogicException("Enter a vehicle number containing 6-15 letters and digits.");
        if (!Regex.IsMatch(screen, "^[A-Z0-9-]{3,50}$"))
            throw new BusinessLogicException("Enter a screen ID of 3-50 letters, digits or hyphens.");
        if (request.Status is not ("Available" or "Assigned" or "Maintenance"))
            throw new BusinessLogicException("Choose a valid driver availability.");

        var duplicate = await db.Drivers.AsNoTracking()
            .Where(d => d.Id != driver.Id && (d.Phone == phone || d.VehicleNumber == vehicle || d.ScreenId == screen))
            .Select(d => new { d.Phone, d.VehicleNumber })
            .FirstOrDefaultAsync(cancellationToken);
        if (duplicate != null)
            throw new BusinessLogicException(duplicate.Phone == phone
                ? "This mobile number is already in the directory."
                : duplicate.VehicleNumber == vehicle
                    ? "This vehicle number is already in the directory."
                    : "This screen ID is already assigned to another driver.");

        driver.FullName = name;
        driver.Phone = phone;
        driver.Email = email;
        driver.VehicleNumber = vehicle;
        driver.VehicleModel = model;
        driver.ServiceArea = area;
        driver.ScreenId = screen;
        driver.Status = request.Status;
    }

    private async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            logger.LogWarning("A driver operation conflicted with an existing unique value.");
            throw new BusinessLogicException("The mobile number, vehicle number, screen ID, assignment or transaction ID is already in use.");
        }
    }

    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || Encoding.UTF8.GetByteCount(password) > 72)
            throw new BusinessLogicException("Generate a password of at least 12 characters and at most 72 bytes.");
    }

    private static string? NormalizePhone(string phone)
    {
        if (!Regex.IsMatch(phone, @"^\+?[\d ()-]+$")) return null;
        var digits = Regex.Replace(phone, @"\D", "");
        if (digits.Length == 12 && digits.StartsWith("91")) digits = digits[2..];
        else if (digits.Length == 11 && digits.StartsWith("0")) digits = digits[1..];
        return Regex.IsMatch(digits, "^[6-9][0-9]{9}$") ? digits : null;
    }

    private static DriverDto MapDriver(Driver d) => new()
    {
        Id = d.Id,
        FullName = d.FullName,
        Phone = d.Phone,
        Email = d.Email,
        VehicleNumber = d.VehicleNumber,
        VehicleModel = d.VehicleModel,
        ServiceArea = d.ServiceArea,
        ScreenId = d.ScreenId,
        Status = d.Status,
        IsActive = d.IsActive,
        CreatedAt = d.CreatedAt,
        UpdatedAt = d.UpdatedAt,
        LastLoginAt = d.LastLoginAt
    };
}
