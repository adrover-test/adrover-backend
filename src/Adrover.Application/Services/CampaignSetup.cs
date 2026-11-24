using System.Globalization;
using Adrover.Domain.Entities;

namespace Adrover.Application.Services;

public static class CampaignSetup
{
    public const string StartDatePassedMessage = "The scheduled start date has already passed and the advertisement setup is not yet complete. The advertisement can no longer be scheduled for the selected start date and must be rescheduled to begin from the next available day.";

    public static bool HasStarted(Advertisement advertisement) =>
        advertisement.FirstPlayedAt.HasValue || advertisement.FirstPlaybackDate.HasValue ||
        advertisement.ActualPlays > 0 || advertisement.Status == AdvertisementStatus.Started;

    public static string? GetValidationMessage(Advertisement advertisement, DateTimeOffset now)
    {
        var startDate = DateOnly.FromDateTime(advertisement.StartDate);
        if (advertisement.Status == AdvertisementStatus.Draft && advertisement.RegistrationProgress != null)
        {
            var progress = RegistrationDraftWorkflow.ReadProgress(advertisement);
            if (!DateOnly.TryParseExact(progress.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out startDate))
                return null;
        }

        var today = DateOnly.FromDateTime(now.ToOffset(TimeSpan.FromHours(5.5)).DateTime);
        if (startDate >= today || HasStarted(advertisement))
            return null;

        var readyOnTime = advertisement.ApprovalAmount > 0 && advertisement.PaidAmount >= advertisement.ApprovalAmount
            && advertisement.ReadyForScheduling && !string.IsNullOrWhiteSpace(advertisement.ContentId)
            && advertisement.ReadyForSchedulingAtUtc.HasValue
            && DateOnly.FromDateTime(new DateTimeOffset(DateTime.SpecifyKind(advertisement.ReadyForSchedulingAtUtc.Value, DateTimeKind.Utc))
                .ToOffset(TimeSpan.FromHours(5.5)).DateTime) <= startDate;

        return readyOnTime ? null : StartDatePassedMessage;
    }

    public static void EnsureNotStarted(Advertisement advertisement)
    {
        if (HasStarted(advertisement))
            throw new Adrover.Application.Exceptions.CampaignStartedException();
    }
}
