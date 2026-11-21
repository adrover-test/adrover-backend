using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Adrover.Domain.Entities;

namespace Adrover.Application.DTOs.Advertisement;

public class RegistrationDraftRequest
{
    public Guid Id { get; set; }
    [Required, MaxLength(200)] public string Name { get; set; } = string.Empty;
    [Required, MaxLength(255)] public string Brand { get; set; } = string.Empty;
    [Required, MaxLength(500)] public string TargetArea { get; set; } = string.Empty;
    [Range(typeof(decimal), "0.01", "86400")] public decimal VideoDurationSeconds { get; set; }
    [MaxLength(12)] public string Days { get; set; } = "7";
    [MaxLength(12)] public string DailyPlays { get; set; } = "1000";
    [MaxLength(12)] public string Cabs { get; set; } = "10";
    [MaxLength(10)] public string StartDate { get; set; } = string.Empty;
    [RegularExpression("^(plan|review|payment)$")] public string Step { get; set; } = "plan";
    public bool Consent { get; set; }

    public Adrover.Domain.Entities.Advertisement CreateAdvertisement(Guid clientId)
    {
        if (Id == Guid.Empty)
            throw new Adrover.Domain.Exceptions.BusinessLogicException("A draft ID is required.");
        return new Adrover.Domain.Entities.Advertisement
        {
            Id = Id,
            ClientId = clientId,
            Name = Name.Trim(),
            Brand = Brand.Trim(),
            TargetArea = TargetArea.Trim(),
            Description = "Registration in progress",
            VideoDurationSeconds = VideoDurationSeconds,
            StartDate = DateTime.UnixEpoch,
            EndDate = DateTime.UnixEpoch,
            Status = AdvertisementStatus.Draft,
            SubmittedAt = DateTime.UtcNow,
            RegistrationProgress = JsonSerializer.Serialize(this)
        };
    }
}

public class RegistrationDraftDto
{
    public Guid Id { get; set; }
    public RegistrationDraftRequest Progress { get; set; } = new();
    public bool HasVideo { get; set; }
    public bool HasBanner { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public bool PaymentStarted { get; set; }
}
