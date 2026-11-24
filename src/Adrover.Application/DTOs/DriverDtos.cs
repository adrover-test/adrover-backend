using System.ComponentModel.DataAnnotations;

namespace Adrover.Application.DTOs.Driver;

public class DriverDetailsRequest
{
    [Required, StringLength(120)]
    public string FullName { get; set; } = string.Empty;
    [Required, StringLength(25)]
    public string Phone { get; set; } = string.Empty;
    [Required, StringLength(254)]
    public string Email { get; set; } = string.Empty;
    [Required, StringLength(20)]
    public string VehicleNumber { get; set; } = string.Empty;
    [Required, StringLength(120)]
    public string VehicleModel { get; set; } = string.Empty;
    [Required, StringLength(120)]
    public string ServiceArea { get; set; } = string.Empty;
    [Required, StringLength(50)]
    public string ScreenId { get; set; } = string.Empty;
    [Required, StringLength(20)]
    public string Status { get; set; } = string.Empty;
}

public sealed class CreateDriverRequest : DriverDetailsRequest
{
    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed class UpdateDriverRequest : DriverDetailsRequest
{
    public string? Password { get; set; }
}

public sealed class SetDriverPasswordRequest
{
    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed class DriverLoginRequest
{
    [Required, StringLength(25)]
    public string Phone { get; set; } = string.Empty;
    [Required]
    public string Password { get; set; } = string.Empty;
}

public sealed class DriverProfileUpdateRequest
{
    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;
    [StringLength(254)]
    public string? Email { get; set; }
    [StringLength(300)]
    public string? Address { get; set; }
}

public sealed class DriverLanguageRequest
{
    [Required, RegularExpression("^(en|hi|te)$")]
    public string PreferredLanguage { get; set; } = string.Empty;
}

public sealed class DriverAssignmentRequest
{
    public Guid AdvertisementId { get; set; }
    public DateTime ScheduledAt { get; set; }
    [Range(1, 1000000)]
    public long TargetPlays { get; set; }
    [Range(typeof(decimal), "0.0001", "10000")]
    public decimal RatePerPlay { get; set; }
}

public sealed class DriverAssignmentStatusRequest
{
    [Required, RegularExpression("^(assigned|paused|completed)$")]
    public string Status { get; set; } = string.Empty;
}

public sealed class DriverPlaybackRequest
{
    public Guid RequestId { get; set; }
    [Range(1, 1000000)]
    public long Plays { get; set; }
    public DateTime PlayedAt { get; set; }
}

public sealed class DriverPayoutRequest
{
    [Required, StringLength(100)]
    public string TransactionId { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.01", "10000000")]
    public decimal Amount { get; set; }
    public DateTime PeriodStart { get; set; }
    public DateTime PeriodEnd { get; set; }
    public DateTime ScheduledAt { get; set; }
}

public sealed class DriverPayoutStatusRequest
{
    [Required, RegularExpression("^(pending|processing|released|failed)$")]
    public string Status { get; set; } = string.Empty;
    [StringLength(200)]
    public string? Reference { get; set; }
}

public sealed class DriverNotificationRequest
{
    [Required, StringLength(255)]
    public string Title { get; set; } = string.Empty;
    [Required, StringLength(2000)]
    public string Message { get; set; } = string.Empty;
}

public sealed class DriverIssueRequest
{
    [Required, RegularExpression("^(ad_not_playing|tablet_not_working|internet|wrong_schedule|payment_not_received|earnings_mismatch|vehicle|login_otp|other)$")]
    public string Category { get; set; } = string.Empty;
    [Required, StringLength(2000, MinimumLength = 10)]
    public string Description { get; set; } = string.Empty;
    public Guid? AdvertisementId { get; set; }
    public Guid? VehicleId { get; set; }
    public DateTime OccurredAt { get; set; }
    [Required, RegularExpression("^[6-9][0-9]{9}$")]
    public string ContactMobile { get; set; } = string.Empty;
    [Required, RegularExpression("^(en|hi|te)$")]
    public string Language { get; set; } = string.Empty;
}

public sealed class DriverIssueResponseRequest
{
    [Required, RegularExpression("^(under_review|in_progress|resolved|rejected)$")]
    public string Status { get; set; } = string.Empty;
    [Required, StringLength(2000)]
    public string Note { get; set; } = string.Empty;
}

public sealed class DriverIssueReopenRequest
{
    [Required, StringLength(2000, MinimumLength = 10)]
    public string Reason { get; set; } = string.Empty;
}

public sealed class DriverDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string VehicleModel { get; set; } = string.Empty;
    public string ServiceArea { get; set; } = string.Empty;
    public string ScreenId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
