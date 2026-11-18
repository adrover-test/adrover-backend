using System.Globalization;
using System.Text.Json;
using Adrover.Application.DTOs.Advertisement;
using Adrover.Domain.Entities;
using Adrover.Domain.Exceptions;

namespace Adrover.Application.Services;

public static class RegistrationDraftWorkflow
{
    public static RegistrationDraftRequest ReadProgress(Advertisement ad) =>
        JsonSerializer.Deserialize<RegistrationDraftRequest>(ad.RegistrationProgress
            ?? throw new InvalidOperationException("Registration progress is missing."))
        ?? throw new InvalidOperationException("Registration progress could not be read.");

    public static void PrepareCheckout(Advertisement ad)
    {
        var progress = ReadProgress(ad);
        if (string.IsNullOrEmpty(ad.VideoUrl) || string.IsNullOrEmpty(ad.ImageUrl))
            throw new BusinessLogicException("Upload both the video and banner before checkout.");
        if (!int.TryParse(progress.Days, out var days) || days < 7 || days > 3650 ||
            !int.TryParse(progress.DailyPlays, out var dailyPlays) || dailyPlays < 1000 || dailyPlays > 10000000 ||
            !int.TryParse(progress.Cabs, out var cabs) || (cabs != 10 && cabs != 20 && cabs != 30) ||
            !DateTime.TryParseExact(progress.StartDate, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var start) || start.Year < 2000 || start.Year > 9990 || !progress.Consent)
            throw new BusinessLogicException("Complete a valid plan, start date and consent before checkout.");
        ad.StartDate = start;
        ad.EndDate = start.AddDays(days);
        ad.TargetPlays = (long)days * dailyPlays;
        ad.AssignedCabs = cabs;
        ad.ApprovalAmount = ad.TargetPlays * 0.30m;
        ad.Description = $"{dailyPlays} plays per day x {days} days. Requested cabs: {cabs}.";
    }
}
