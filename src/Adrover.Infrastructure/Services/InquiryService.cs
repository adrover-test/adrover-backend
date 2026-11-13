using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Adrover.Backend.Services;

public sealed class InquiryService(
    IHttpClientFactory clients,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    ILogger<InquiryService> logger)
{
    public async Task<List<(string Channel, bool Accepted, bool Simulated, string? Error)>> SendAsync(
        IReadOnlyDictionary<string, string> inquiry,
        string inquiryId,
        CancellationToken cancellationToken)
    {
        var results = new List<(string Channel, bool Accepted, bool Simulated, string? Error)>();
        var mode = configuration["Notifications:Mode"];
        if (mode == "Simulation" && environment.IsDevelopment())
        {
            foreach (var channel in new[] { "email", "whatsapp", "sms" })
            {
                cancellationToken.ThrowIfCancellationRequested();
                logger.LogInformation(
                    "SIMULATED inquiry {InquiryId}, {Channel}. No provider request or real delivery.",
                    inquiryId, channel);
                results.Add((channel, false, true, null));
            }
            return results;
        }
        if (mode != "Live" || environment.IsDevelopment())
        {
            logger.LogWarning("Inquiry {InquiryId}: real notifications are disabled.", inquiryId);
            return new[] { "email", "whatsapp", "sms" }
                .Select(channel => (channel, false, false, (string?)"Real notifications are disabled."))
                .ToList();
        }
        var http = clients.CreateClient("notifications");
        foreach (var channel in new[] { "email", "whatsapp", "sms" })
        {
            try
            {
                var error = channel switch
                {
                    "email" => await SendEmailAsync(http, inquiry, inquiryId, cancellationToken),
                    "whatsapp" => await SendWhatsAppAsync(http, inquiry, cancellationToken),
                    _ => await SendSmsAsync(http, inquiry, cancellationToken)
                };
                results.Add((channel, error is null, false, error));
                if (error is not null)
                {
                    logger.LogWarning("Inquiry {InquiryId}, {Channel}: {Error}", inquiryId, channel, error);
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or JsonException or OperationCanceledException
                && !cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning("Inquiry {InquiryId}, {Channel}: provider failure {FailureType}.",
                    inquiryId, channel, exception.GetType().Name);
                results.Add((channel, false, false,
                    "Provider request failed or timed out. Acceptance is unknown; do not blindly retry."));
            }
        }
        return results;
    }

    private async Task<string?> SendEmailAsync(
        HttpClient http, IReadOnlyDictionary<string, string> inquiry, string id, CancellationToken token)
    {
        var tenant = configuration["Outlook:TenantId"];
        var client = configuration["Outlook:ClientId"];
        var secret = configuration["Outlook:ClientSecret"];
        var sender = configuration["Outlook:SenderEmail"];
        var admin = configuration["Outlook:AdminEmail"];
        if (new[] { tenant, client, secret }.Any(string.IsNullOrWhiteSpace)
            || !MailAddress.TryCreate(sender, out _)
            || !MailAddress.TryCreate(admin, out var recipient))
        {
            return "Outlook credentials or admin/sender email are not configured correctly.";
        }

        using var tokenResponse = await http.PostAsync(
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenant!)}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = client!,
                ["client_secret"] = secret!,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials"
            }), token);
        if (!tokenResponse.IsSuccessStatusCode)
        {
            return Failure("Outlook authentication", tokenResponse);
        }
        using var auth = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync(token));
        if (auth.RootElement.ValueKind != JsonValueKind.Object
            || !auth.RootElement.TryGetProperty("access_token", out var accessToken)
            || accessToken.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(accessToken.GetString()))
        {
            return "Outlook returned no access token.";
        }

        var body = string.Join("\n", new[]
        {
            $"New Adrover campaign inquiry: {id}",
            $"Your name: {inquiry["name"]}",
            $"Business name: {inquiry["business"]}",
            $"Work email: {inquiry["email"]}",
            $"Phone / WhatsApp: {Display(inquiry["phone"])}",
            $"Campaign objective: {inquiry["goal"]}",
            $"Preferred screen format: {inquiry["format"]}",
            $"Anything we should know?: {Display(inquiry["message"])}"
        });
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(sender!)}/sendMail")
        {
            Content = JsonContent.Create(new
            {
                message = new
                {
                    subject = $"Adrover inquiry - {inquiry["business"]}",
                    body = new { contentType = "Text", content = body },
                    toRecipients = new[] { new { emailAddress = new { address = recipient.Address } } },
                    replyTo = new[] { new { emailAddress = new { address = inquiry["email"] } } }
                },
                saveToSentItems = true
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken.GetString());
        using var response = await http.SendAsync(request, token);
        return response.IsSuccessStatusCode ? null : Failure("Outlook", response);
    }

    private async Task<string?> SendWhatsAppAsync(
        HttpClient http, IReadOnlyDictionary<string, string> inquiry, CancellationToken token)
    {
        var accessToken = configuration["WhatsApp:AccessToken"];
        var phoneId = configuration["WhatsApp:PhoneNumberId"];
        var template = configuration["WhatsApp:TemplateName"];
        var language = configuration["WhatsApp:LanguageCode"];
        var version = configuration["WhatsApp:ApiVersion"];
        var recipient = NormalizePhone(configuration["WhatsApp:AdminPhone"]);
        if (new[] { accessToken, phoneId, template, language, version, recipient }.Any(string.IsNullOrWhiteSpace))
        {
            return "WhatsApp credentials, approved template, language, version or admin phone are not configured correctly.";
        }

        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://graph.facebook.com/{Uri.EscapeDataString(version!)}/{Uri.EscapeDataString(phoneId!)}/messages")
        {
            Content = JsonContent.Create(new
            {
                messaging_product = "whatsapp",
                to = recipient,
                type = "template",
                template = new
                {
                    name = template,
                    language = new { code = language },
                    components = new[]
                    {
                        new
                        {
                            type = "body",
                            parameters = new[]
                            {
                                inquiry["name"], inquiry["business"], inquiry["email"],
                                Display(inquiry["phone"]), inquiry["goal"], inquiry["format"]
                            }.Select(text => new { type = "text", text }).ToArray()
                        }
                    }
                }
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            return Failure("WhatsApp", response);
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("messages", out var messages)
            && messages.ValueKind == JsonValueKind.Array
            && messages.GetArrayLength() > 0
            && messages[0].ValueKind == JsonValueKind.Object
            && messages[0].TryGetProperty("id", out var messageId)
            && messageId.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(messageId.GetString())
                ? null : "WhatsApp did not return a message acceptance ID.";
    }

    private async Task<string?> SendSmsAsync(
        HttpClient http, IReadOnlyDictionary<string, string> inquiry, CancellationToken token)
    {
        var authKey = configuration["Msg91:AuthKey"];
        var template = configuration["Msg91:TemplateId"];
        var recipient = NormalizePhone(configuration["Msg91:AdminPhone"]);
        if (new[] { authKey, template, recipient }.Any(string.IsNullOrWhiteSpace)
            || !Regex.IsMatch(recipient!, "^91[6-9][0-9]{9}$"))
        {
            return "MSG91 credentials, DLT template or admin phone are not configured correctly.";
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://control.msg91.com/api/v5/flow")
        {
            Content = JsonContent.Create(new
            {
                template_id = template,
                short_url = "0",
                recipients = new[]
                {
                    new
                    {
                        mobiles = recipient,
                        name = inquiry["name"],
                        business = inquiry["business"],
                        email = inquiry["email"],
                        phone = Display(inquiry["phone"]),
                        goal = inquiry["goal"],
                        format = inquiry["format"]
                    }
                }
            })
        };
        request.Headers.Add("authkey", authKey);
        using var response = await http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode)
        {
            return Failure("MSG91", response);
        }
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String
            && type.GetString() == "success"
                ? null : "MSG91 did not accept the SMS.";
    }

    private static string Failure(string provider, HttpResponseMessage response) =>
        $"{provider} rejected the request (HTTP {(int)response.StatusCode}).";

    private static string Display(string value) => string.IsNullOrWhiteSpace(value) ? "Not provided" : value;

    public static string? NormalizePhone(string? value)
    {
        var digits = Regex.Replace(value ?? "", @"[\s()+-]", "");
        if (Regex.IsMatch(digits, "^[6-9][0-9]{9}$"))
        {
            return $"91{digits}";
        }
        return Regex.IsMatch(digits, "^[1-9][0-9]{10,14}$") ? digits : null;
    }
}
