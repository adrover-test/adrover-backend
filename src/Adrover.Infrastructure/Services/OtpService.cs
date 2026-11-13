using Microsoft.Extensions.Caching.Memory;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Adrover.Backend.Services;

public sealed class OtpService(
    IMemoryCache cache,
    IHttpClientFactory clients,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    TimeProvider clock,
    ILogger<OtpService> logger) : IDisposable
{
    private readonly byte[] hashKey = RandomNumberGenerator.GetBytes(32);
    private readonly SemaphoreSlim[] gates = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1)).ToArray();

    public async Task<(int StatusCode, object Body)> SendAsync(string mobile, CancellationToken token)
    {
        var mode = configuration["Otp:Mode"];
        var simulated = mode == "Simulation" && environment.IsDevelopment();
        if (!simulated && (mode != "Live" || environment.IsDevelopment()))
        {
            logger.LogWarning("OTP sending is disabled.");
            return (503, new { error = "OTP sending is disabled." });
        }
        var gate = Gate(mobile);
        await gate.WaitAsync(token);
        try
        {
            var now = clock.GetUtcNow();
            cache.TryGetValue<(DateTimeOffset NextSend, DateTimeOffset WindowEnd, int Sends)>(
                $"rate:{mobile}", out var rate);
            if (now < rate.NextSend || (now < rate.WindowEnd && rate.Sends >= 5))
            {
                var retryAt = rate.Sends >= 5 ? rate.WindowEnd : rate.NextSend;
                return (429, new { error = "OTP send limit reached.", retryAfterSeconds = (int)Math.Ceiling((retryAt - now).TotalSeconds) });
            }
            // Reserve the send budget even on failure: a timeout may occur after SMS acceptance.
            var windowEnd = now < rate.WindowEnd ? rate.WindowEnd : now.AddHours(1);
            var sends = now < rate.WindowEnd ? rate.Sends + 1 : 1;
            cache.Set($"rate:{mobile}", (now.AddSeconds(60), windowEnd, sends),
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = windowEnd - now, Size = 1 });
            var otp = RandomNumberGenerator.GetInt32(100000, 1000000).ToString();
            cache.Remove($"otp:{mobile}");

            if (!simulated)
            {
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post,
                        "https://control.msg91.com/api/v5/otp"
                        + $"?template_id={Uri.EscapeDataString(configuration["Msg91:OtpTemplateId"]!)}"
                        + $"&mobile=91{mobile}&otp={otp}&otp_length=6&otp_expiry=5");
                    request.Headers.Add("authkey", configuration["Msg91:AuthKey"]);
                    using var response = await clients.CreateClient("notifications").SendAsync(request, token);
                    if (!response.IsSuccessStatusCode)
                    {
                        logger.LogWarning("MSG91 OTP request rejected with HTTP {StatusCode}.", (int)response.StatusCode);
                        return (502, new { error = "MSG91 did not accept the OTP. Wait before requesting another." });
                    }
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
                    if (document.RootElement.ValueKind != JsonValueKind.Object
                        || !document.RootElement.TryGetProperty("type", out var type)
                        || type.ValueKind != JsonValueKind.String || type.GetString() != "success")
                    {
                        logger.LogWarning("MSG91 OTP response did not confirm acceptance.");
                        return (502, new { error = "MSG91 did not confirm OTP acceptance." });
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or JsonException or OperationCanceledException
                    && !token.IsCancellationRequested)
                {
                    logger.LogWarning("MSG91 OTP request failed: {FailureType}.", exception.GetType().Name);
                    return (502, new { error = "OTP provider request failed; acceptance is unknown. Wait before requesting another." });
                }
            }
            var expiresAt = clock.GetUtcNow().AddMinutes(5);
            cache.Set($"otp:{mobile}", (Hash(mobile, otp), expiresAt, 0),
                new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5), Size = 1 });
            if (simulated)
            {
                logger.LogInformation("OTP simulated; no SMS was sent.");
                return (200, new { status = "simulated", simulated = true, expiresAt, expiresInSeconds = 300, resendAfterSeconds = 60, demoOtp = otp });
            }
            return (200, new { status = "accepted", simulated = false, expiresAt, expiresInSeconds = 300, resendAfterSeconds = 60 });
        }
        finally { gate.Release(); }
    }

    public async Task<(int StatusCode, object Body)> VerifyAsync(string mobile, string otp, CancellationToken token)
    {
        var mode = configuration["Otp:Mode"];
        if (!(mode == "Simulation" && environment.IsDevelopment())
            && !(mode == "Live" && !environment.IsDevelopment()))
        {
            return (503, new { verified = false, error = "OTP verification is disabled." });
        }
        var gate = Gate(mobile);
        await gate.WaitAsync(token);
        try
        {
            var now = clock.GetUtcNow();
            if (!cache.TryGetValue<(byte[] Hash, DateTimeOffset ExpiresAt, int Attempts)>($"otp:{mobile}", out var state)
                || now >= state.ExpiresAt)
            {
                cache.Remove($"otp:{mobile}");
                return (400, new { verified = false, error = "OTP expired, used or unavailable. Request a new OTP." });
            }
            if (CryptographicOperations.FixedTimeEquals(state.Hash, Hash(mobile, otp)))
            {
                cache.Remove($"otp:{mobile}");
                return (200, new { verified = true, simulated = mode == "Simulation" });
            }
            var attempts = state.Attempts + 1;
            if (attempts >= 5)
            {
                cache.Remove($"otp:{mobile}");
            }
            else
            {
                cache.Set($"otp:{mobile}", (state.Hash, state.ExpiresAt, attempts),
                    new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = state.ExpiresAt - now, Size = 1 });
            }
            return (400, new { verified = false, error = attempts >= 5 ? "Too many incorrect attempts. Request a new OTP." : "Incorrect OTP.", attemptsRemaining = 5 - attempts });
        }
        finally { gate.Release(); }
    }

    private byte[] Hash(string mobile, string otp) =>
        HMACSHA256.HashData(hashKey, Encoding.UTF8.GetBytes($"{mobile}:{otp}"));

    private SemaphoreSlim Gate(string mobile) => gates[int.Parse(mobile[^2..]) % gates.Length];

    public void Dispose()
    {
        foreach (var gate in gates) { gate.Dispose(); }
        CryptographicOperations.ZeroMemory(hashKey);
    }
}
