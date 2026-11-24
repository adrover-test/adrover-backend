using System.Text.Json.Serialization;

namespace Adrover.Functions.Intelisa;

public sealed record IntelisaScreen(
    [property: JsonPropertyName("screenID")] string ScreenId,
    [property: JsonPropertyName("screenName")] string Name,
    [property: JsonPropertyName("spotCode")] string? SpotCode,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("lastDataRecorded")] string? LastDataRecorded,
    [property: JsonPropertyName("tags")] string[]? Tags);

public sealed record CreativeReport(
    [property: JsonPropertyName("totals")] ReportTotals? Totals,
    [property: JsonPropertyName("per_day")] ReportDay[]? PerDay,
    [property: JsonPropertyName("dates")] string[]? Dates,
    [property: JsonPropertyName("screens")] ReportScreen[]? Screens,
    [property: JsonPropertyName("data_freshness")] ReportFreshness? DataFreshness);

public sealed record ReportTotals(
    [property: JsonPropertyName("plays")] long Plays,
    [property: JsonPropertyName("minutes")] decimal Minutes);

public sealed record ReportDay(
    [property: JsonPropertyName("date")] string Date,
    [property: JsonPropertyName("plays")] long Plays,
    [property: JsonPropertyName("minutes")] decimal Minutes);

public sealed record ReportScreen(
    [property: JsonPropertyName("screen_id")] string ScreenId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("plays")] long Plays,
    [property: JsonPropertyName("daily_plays")] long[]? DailyPlays);

public sealed record ReportFreshness(
    [property: JsonPropertyName("latest_day_with_play_data")] string? LatestDayWithPlayData);

public sealed record CreativeStatusResponse(
    [property: JsonPropertyName("creatives")] CreativeStatus[] Creatives);

public sealed record CreativeStatus(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("status")] string Status);

public sealed record CreativeUploadResult(string ContentId);

public sealed class IntelisaRequestException(HttpStatusCode statusCode, string message, bool outcomeMayBeUnknown = false)
    : Exception(message)
{
    public HttpStatusCode StatusCode { get; } = statusCode;
    public bool OutcomeMayBeUnknown { get; } = outcomeMayBeUnknown;
}
