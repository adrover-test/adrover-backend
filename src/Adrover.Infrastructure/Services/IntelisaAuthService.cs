using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace Adrover.Backend.Services;

public sealed class IntelisaAuthService(
    IHttpClientFactory clients,
    IConfiguration configuration,
    ILogger<IntelisaAuthService> logger)
{
    public async Task<(int StatusCode, object Body)> LoginAsync(CancellationToken token)
    {
        var baseUrl = (configuration["Intelisa:BaseUrl"] ?? "").Trim().TrimEnd('/');
        var userName = configuration["Intelisa:UserName"];
        var password = configuration["Intelisa:Password"];

        if (string.IsNullOrWhiteSpace(baseUrl)
            || string.IsNullOrWhiteSpace(userName)
            || string.IsNullOrWhiteSpace(password))
        {
            return (503, new
            {
                error = "Intelisa login is not configured.",
                required = new[] { "Intelisa:BaseUrl", "Intelisa:UserName", "Intelisa:Password" }
            });
        }

        try
        {
            using var response = await clients.CreateClient("notifications").PostAsJsonAsync(
                $"{baseUrl}/api/v1/auth/login",
                new { userName, password },
                token);

            var content = await response.Content.ReadAsStringAsync(token);
            var body = ParseJsonOrText(content);

            if (response.IsSuccessStatusCode)
            {
                return ((int)response.StatusCode, body);
            }

            logger.LogWarning("Intelisa login rejected with HTTP {StatusCode}.", (int)response.StatusCode);
            return ((int)response.StatusCode, body);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException
            && !token.IsCancellationRequested)
        {
            logger.LogWarning("Intelisa login request failed: {FailureType}.", exception.GetType().Name);
            return (502, new
            {
                error = "Intelisa login request failed.",
                message = "Provider request failed or timed out."
            });
        }
    }

    private static object ParseJsonOrText(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return new { message = "Empty response." };
        }

        try
        {
            using var document = JsonDocument.Parse(content);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return new { message = content };
        }
    }
}
