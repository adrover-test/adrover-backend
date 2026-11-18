using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Adrover.Infrastructure.Services;

public sealed record RazorpayOrder(string Id, int Amount, string Currency, string Status);

public sealed record RazorpayPaymentDetails(
    string Id,
    string OrderId,
    int Amount,
    string Currency,
    string Status,
    string Method,
    string? CardLastFour,
    string? CardIssuer);

public interface IPaymentGatewayService
{
    string KeyId { get; }
    Task<RazorpayOrder> CreateOrderAsync(
        decimal amount,
        string currency,
        string receipt,
        Dictionary<string, string> notes);
    bool VerifyPaymentSignature(string orderId, string paymentId, string signature);
    bool VerifyWebhookSignature(string payload, string signature);
    Task<RazorpayPaymentDetails> GetPaymentDetailsAsync(string paymentId);
    Task<IReadOnlyList<RazorpayPaymentDetails>> GetOrderPaymentsAsync(string orderId);
    Task<RazorpayPaymentDetails> CapturePaymentAsync(string paymentId, int amount, string currency);
}

public class RazorpayPaymentService : IPaymentGatewayService
{
    private const string RazorpayApiUrl = "https://api.razorpay.com/v1";
    private readonly string _keyId;
    private readonly string _keySecret;
    private readonly string _webhookSecret;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RazorpayPaymentService> _logger;

    public RazorpayPaymentService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<RazorpayPaymentService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _keyId = configuration["Razorpay:KeyId"] ?? string.Empty;
        _keySecret = configuration["Razorpay:KeySecret"] ?? string.Empty;
        _webhookSecret = configuration["Razorpay:WebhookSecret"] ?? string.Empty;
    }

    public string KeyId => _keyId;

    public async Task<RazorpayOrder> CreateOrderAsync(
        decimal amount,
        string currency,
        string receipt,
        Dictionary<string, string> notes)
    {
        EnsureApiCredentials();

        var amountInMinorUnits = amount * 100m;
        if (amountInMinorUnits <= 0 ||
            amountInMinorUnits != decimal.Truncate(amountInMinorUnits) ||
            amountInMinorUnits > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Amount must be a positive, exact number of paise and fit in a 32-bit integer.");
        }

        var payload = new
        {
            amount = decimal.ToInt32(amountInMinorUnits),
            currency,
            receipt,
            notes
        };

        using var response = await SendAsync(HttpMethod.Post, "/orders", payload, _keySecret);
        using var document = await ReadJsonAsync(response);
        var root = document.RootElement;

        return new RazorpayOrder(
            root.GetProperty("id").GetString() ?? throw new JsonException("Razorpay order response has no ID."),
            root.GetProperty("amount").GetInt32(),
            root.GetProperty("currency").GetString() ?? currency,
            root.GetProperty("status").GetString() ?? string.Empty);
    }

    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
    {
        EnsureApiCredentials();
        return VerifyHmac(_keySecret, $"{orderId}|{paymentId}", signature);
    }

    public bool VerifyWebhookSignature(string payload, string signature)
    {
        if (string.IsNullOrWhiteSpace(_webhookSecret))
            throw new InvalidOperationException("Razorpay webhook secret is not configured.");

        return VerifyHmac(_webhookSecret, payload, signature);
    }

    public async Task<RazorpayPaymentDetails> GetPaymentDetailsAsync(string paymentId)
    {
        EnsureApiCredentials();

        using var response = await SendAsync(
            HttpMethod.Get,
            $"/payments/{Uri.EscapeDataString(paymentId)}",
            null,
            _keySecret);
        using var document = await ReadJsonAsync(response);
        return ParsePayment(document.RootElement);
    }

    public async Task<IReadOnlyList<RazorpayPaymentDetails>> GetOrderPaymentsAsync(string orderId)
    {
        EnsureApiCredentials();

        using var response = await SendAsync(
            HttpMethod.Get,
            $"/orders/{Uri.EscapeDataString(orderId)}/payments",
            null,
            _keySecret);
        using var document = await ReadJsonAsync(response);
        return document.RootElement.GetProperty("items").EnumerateArray().Select(ParsePayment).ToList();
    }

    public async Task<RazorpayPaymentDetails> CapturePaymentAsync(string paymentId, int amount, string currency)
    {
        EnsureApiCredentials();
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Capture amount must be positive.");

        try
        {
            using var response = await SendAsync(
                HttpMethod.Post,
                $"/payments/{Uri.EscapeDataString(paymentId)}/capture",
                new { amount, currency },
                _keySecret);
            using var document = await ReadJsonAsync(response);
            return ParsePayment(document.RootElement);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Dashboard auto-capture or another callback may have captured it first.
            var payment = await GetPaymentDetailsAsync(paymentId);
            if (payment.Status is "captured" or "refunded")
                return payment;

            throw;
        }
    }

    private static RazorpayPaymentDetails ParsePayment(JsonElement root)
    {
        string? cardLastFour = null;
        string? cardIssuer = null;
        if (root.TryGetProperty("card", out var card) && card.ValueKind == JsonValueKind.Object)
        {
            cardLastFour = GetStringOrNull(card, "last4");
            cardIssuer = GetStringOrNull(card, "issuer");
        }

        return new RazorpayPaymentDetails(
            root.GetProperty("id").GetString() ?? throw new JsonException("Razorpay payment response has no ID."),
            root.GetProperty("order_id").GetString() ?? string.Empty,
            root.GetProperty("amount").GetInt32(),
            root.GetProperty("currency").GetString() ?? string.Empty,
            root.GetProperty("status").GetString() ?? string.Empty,
            root.TryGetProperty("method", out var method) ? method.GetString() ?? string.Empty : string.Empty,
            cardLastFour,
            cardIssuer);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? payload,
        string secret)
    {
        using var request = new HttpRequestMessage(method, $"{RazorpayApiUrl}{path}");
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_keyId}:{secret}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", token);
        if (payload != null)
            request.Content = JsonContent.Create(payload);

        var response = await _httpClient.SendAsync(request);
        if (response.IsSuccessStatusCode)
            return response;

        _logger.LogError("Razorpay API returned HTTP {StatusCode}", (int)response.StatusCode);
        var statusCode = response.StatusCode;
        response.Dispose();
        throw new HttpRequestException($"Razorpay API request failed with status {(int)statusCode}.", null, statusCode);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    private static string? GetStringOrNull(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetString()
            : null;
    }

    private static bool VerifyHmac(string secret, string data, string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
            return false;

        byte[] providedSignature;
        try
        {
            providedSignature = Convert.FromHexString(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expectedSignature = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return providedSignature.Length == expectedSignature.Length &&
            CryptographicOperations.FixedTimeEquals(providedSignature, expectedSignature);
    }

    private void EnsureApiCredentials()
    {
        if (string.IsNullOrWhiteSpace(_keyId) || string.IsNullOrWhiteSpace(_keySecret))
            throw new InvalidOperationException("Razorpay API credentials are not configured.");
    }
}
