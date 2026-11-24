using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Adrover.Functions.Configuration;
using Microsoft.Extensions.Options;

namespace Adrover.Functions.Intelisa;

public sealed class IntelisaClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly IntelisaOptions _options;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset _tokenExpiresAt;

    public IntelisaClient(HttpClient httpClient, IOptions<IntelisaOptions> options)
    {
        this.httpClient = httpClient;
        _options = options.Value;
        this.httpClient.BaseAddress = new Uri($"{_options.BaseUrl.TrimEnd('/')}/", UriKind.Absolute);
    }

    public async Task<CreativeUploadResult> UploadCreativeAsync(
        Stream stream,
        string fileName,
        string adGroup,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage? response = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (attempt > 0)
            {
                if (!stream.CanSeek)
                    throw new IntelisaRequestException(HttpStatusCode.Unauthorized, "Intelisa authentication expired during upload; the creative must be reconciled before retry.", outcomeMayBeUnknown: true);
                stream.Position = 0;
            }
            var token = await GetTokenAsync(attempt > 0, cancellationToken);
            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(new NonDisposingStream(stream));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "file", fileName);
            form.Add(new StringContent(adGroup), "adGroup");
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/creatives/upload") { Content = form };
            request.Headers.Add("auth-token", token);
            try
            {
                response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IntelisaRequestException(HttpStatusCode.RequestTimeout, "Intelisa upload timed out; its outcome is unknown.", outcomeMayBeUnknown: true);
            }
            catch (HttpRequestException)
            {
                throw new IntelisaRequestException(HttpStatusCode.ServiceUnavailable, "Intelisa upload failed before a response was received; its outcome is unknown.", outcomeMayBeUnknown: true);
            }
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt != 0)
                break;
            response.Dispose();
            response = null;
        }

        using (response)
        {
            await EnsureSuccessAsync(response!, cancellationToken, outcomeMayBeUnknown: true);
            using var json = await JsonDocument.ParseAsync(await response!.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            var id = json.RootElement.GetProperty("creative").GetProperty("id").GetString();
            if (string.IsNullOrWhiteSpace(id))
                throw new InvalidDataException("Intelisa upload response did not contain a creative ID.");
            return new CreativeUploadResult(id);
        }
    }

    public async Task<string> GetCreativeStatusAsync(string contentId, CancellationToken cancellationToken)
    {
        var response = await PostJsonAsync<CreativeStatusResponse>("/api/v1/creatives/status",
            new { creative_ids = new[] { contentId } }, cancellationToken);
        return response.Creatives.FirstOrDefault(item => item.Id == contentId)?.Status ?? "not_found";
    }

    public Task<IntelisaScreen[]> GetScreensAsync(CancellationToken cancellationToken) =>
        PostJsonAsync<IntelisaScreen[]>("/api/v1/screens", new { }, cancellationToken);

    public Task<CreativeReport> GetCreativeReportAsync(
        string contentId,
        DateOnly startDate,
        DateOnly endDate,
        CancellationToken cancellationToken) =>
        PostJsonAsync<CreativeReport>("/api/v1/reports/creative-detail", new
        {
            creative_id = contentId,
            start_date = startDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            end_date = endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        }, cancellationToken);

    public async Task ScheduleAsync(object schedule, bool dryRun, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToElement(schedule, JsonOptions);
        var requestPayload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(payload.GetRawText(), JsonOptions)
            ?? throw new InvalidDataException("Invalid schedule payload.");
        if (dryRun)
            requestPayload["dry_run"] = JsonSerializer.SerializeToElement(true);
        using var response = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Post, "/api/v1/schedules")
        {
            Content = JsonContent.Create(requestPayload, options: JsonOptions)
        }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken, outcomeMayBeUnknown: !dryRun);
    }

    private async Task<T> PostJsonAsync<T>(string path, object payload, CancellationToken cancellationToken)
    {
        using var response = await SendAuthorizedAsync(() => new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(payload, options: JsonOptions)
        }, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken);
        return result ?? throw new InvalidDataException($"Intelisa returned an empty response for {path}.");
    }

    private async Task<HttpResponseMessage> SendAuthorizedAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetTokenAsync(attempt > 0, cancellationToken);
            using var request = requestFactory();
            request.Headers.Add("auth-token", token);
            try
            {
                var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (response.StatusCode != HttpStatusCode.Unauthorized || attempt != 0)
                    return response;
                response.Dispose();
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IntelisaRequestException(HttpStatusCode.RequestTimeout, "Intelisa request timed out.", outcomeMayBeUnknown: true);
            }
            catch (HttpRequestException)
            {
                throw new IntelisaRequestException(HttpStatusCode.ServiceUnavailable, "Intelisa request failed before a response was received.", outcomeMayBeUnknown: true);
            }
        }
        throw new IntelisaRequestException(HttpStatusCode.Unauthorized, "Intelisa authentication failed after refreshing the token.");
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _token is not null && _tokenExpiresAt > DateTimeOffset.UtcNow)
            return _token;

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _token is not null && _tokenExpiresAt > DateTimeOffset.UtcNow)
                return _token;
            if (string.IsNullOrWhiteSpace(_options.UserName) || string.IsNullOrWhiteSpace(_options.Password))
                throw new InvalidOperationException("Intelisa:UserName and Intelisa:Password are required.");

            using var response = await httpClient.PostAsJsonAsync("/api/v1/auth/login", new
            {
                userName = _options.UserName,
                password = _options.Password
            }, JsonOptions, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new IntelisaRequestException(response.StatusCode, "Intelisa authentication failed.");
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            _token = json.RootElement.GetProperty("token").GetString();
            if (string.IsNullOrWhiteSpace(_token))
                throw new InvalidDataException("Intelisa login response did not contain a token.");
            _tokenExpiresAt = DateTimeOffset.UtcNow.AddHours(23);
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private sealed class NonDisposingStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => false;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { }
        public override ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken,
        bool outcomeMayBeUnknown = false)
    {
        if (response.IsSuccessStatusCode)
            return;
        var statusCode = response.StatusCode;
        var message = $"Intelisa returned HTTP {(int)statusCode}.";
        try
        {
            using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
            if (json.RootElement.TryGetProperty("message", out var detail) && detail.ValueKind == JsonValueKind.String)
                message = detail.GetString() ?? message;
        }
        catch (JsonException)
        {
        }
        throw new IntelisaRequestException(statusCode, message, outcomeMayBeUnknown && ((int)statusCode >= 500 || statusCode == HttpStatusCode.RequestTimeout));
    }
}
