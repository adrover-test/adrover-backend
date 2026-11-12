using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Adrover.Infrastructure.Services;

/// <summary>
/// Razorpay payment gateway integration
/// </summary>
public interface IPaymentGatewayService
{
    Task<Dictionary<string, object>> CreateOrderAsync(decimal amount, string description, Dictionary<string, string> metadata);
    bool VerifyPaymentSignature(string orderId, string paymentId, string signature);
    Task<Dictionary<string, object>> GetPaymentDetailsAsync(string paymentId);
}

public class RazorpayPaymentService : IPaymentGatewayService
{
    private readonly string _keyId;
    private readonly string _keySecret;
    private readonly HttpClient _httpClient;
    private readonly ILogger<RazorpayPaymentService> _logger;
    private const string RazorpayApiUrl = "https://api.razorpay.com/v1";

    public RazorpayPaymentService(string keyId, string keySecret, HttpClient httpClient, ILogger<RazorpayPaymentService> logger)
    {
        _keyId = keyId;
        _keySecret = keySecret;
        _httpClient = httpClient;
        _logger = logger;
        
        var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{keyId}:{keySecret}"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);
    }

    public async Task<Dictionary<string, object>> CreateOrderAsync(decimal amount, string description, Dictionary<string, string> metadata)
    {
        try
        {
            var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                { "amount", ((int)(amount * 100)).ToString() },
                { "currency", "INR" },
                { "description", description }
            });

            var response = await _httpClient.PostAsync($"{RazorpayApiUrl}/orders", content);
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseContent);
            var root = jsonDoc.RootElement;

            return new Dictionary<string, object>
            {
                { "id", root.GetProperty("id").GetString() ?? "" },
                { "amount", amount },
                { "currency", root.GetProperty("currency").GetString() ?? "INR" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating Razorpay order");
            throw new ApplicationException("Failed to create payment order", ex);
        }
    }

    public bool VerifyPaymentSignature(string orderId, string paymentId, string signature)
    {
        try
        {
            var input = $"{orderId}|{paymentId}";
            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_keySecret)))
            {
                var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
                var computedSignature = Convert.ToHexString(hash).ToLower();
                return computedSignature == signature.ToLower();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying payment signature");
            return false;
        }
    }

    public async Task<Dictionary<string, object>> GetPaymentDetailsAsync(string paymentId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"{RazorpayApiUrl}/payments/{paymentId}");
            response.EnsureSuccessStatusCode();

            var responseContent = await response.Content.ReadAsStringAsync();
            var jsonDoc = JsonDocument.Parse(responseContent);
            var root = jsonDoc.RootElement;

            return new Dictionary<string, object>
            {
                { "id", root.GetProperty("id").GetString() ?? "" },
                { "amount", root.GetProperty("amount").GetInt32() },
                { "status", root.GetProperty("status").GetString() ?? "" },
                { "method", root.GetProperty("method").GetString() ?? "" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching payment details");
            throw new ApplicationException("Failed to fetch payment details", ex);
        }
    }
}
