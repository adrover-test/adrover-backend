using System.Net.Http.Headers;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.AddHttpClient("providers", client => client.Timeout = TimeSpan.FromSeconds(20));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 30,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseRateLimiter();

app.MapPost("/api/messages", async (
    SendMessageRequest request,
    HttpRequest httpRequest,
    IConfiguration config,
    IHttpClientFactory httpClientFactory,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var apiKey = config["Messaging:ApiKey"];
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        return Results.Problem("Messaging:ApiKey is not configured.", statusCode: 503);
    }
    if (!FixedEquals(httpRequest.Headers["X-Api-Key"].ToString(), apiKey))
    {
        return Results.Problem("A valid X-Api-Key header is required.", statusCode: 401);
    }

    var channels = (request.Channels ?? [])
        .Select(channel => channel.Trim().ToLowerInvariant())
        .Distinct()
        .ToArray();
    var errors = new Dictionary<string, string[]>();
    if (channels.Length == 0 || channels.Any(channel => channel is not ("email" or "whatsapp" or "sms")))
    {
        errors["channels"] = ["Use one or more of: email, whatsapp, sms."];
    }
    if (string.IsNullOrWhiteSpace(request.Message) && string.IsNullOrWhiteSpace(request.TemplateName))
    {
        errors["message"] = ["Provide message text or templateName."];
    }
    if (request.Message?.Length > 4096)
    {
        errors["message"] = ["Message can contain up to 4096 characters."];
    }
    if (channels.Contains("email") && string.IsNullOrWhiteSpace(request.Message))
    {
        errors["message"] = ["Message is required for email."];
    }
    var phone = "";
    if ((channels.Contains("whatsapp") || channels.Contains("sms")) && !TryPhone(request.To, out phone))
    {
        errors["to"] = ["Enter a 10-digit Indian mobile number or an international number with country code."];
    }
    foreach (var key in request.Variables?.Keys.ToArray() ?? [])
    {
        if (!Regex.IsMatch(key, "^[A-Za-z0-9_]{1,50}$") || key.Equals("mobiles", StringComparison.OrdinalIgnoreCase))
        {
            errors["variables"] = [$"Invalid SMS variable name '{key}'."];
        }
    }
    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var http = httpClientFactory.CreateClient("providers");
    var results = new List<ChannelResult>();
    foreach (var channel in channels)
    {
        try
        {
            results.Add(channel switch
            {
                "email" => await SendEmail(),
                "whatsapp" => await SendWhatsApp(),
                _ => await SendSms(),
            });
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(exception, "Provider request failed for {Channel}.", channel);
            results.Add(new ChannelResult(channel, false, Error: "The provider could not be reached."));
        }
    }

    var response = new { sent = results.All(result => result.Sent), results };
    return results.Any(result => result.Sent)
        ? Results.Ok(response)
        : Results.Json(response, statusCode: StatusCodes.Status502BadGateway);

    async Task<ChannelResult> SendEmail()
    {
        var tenantId = config["Outlook:TenantId"];
        var clientId = config["Outlook:ClientId"];
        var clientSecret = config["Outlook:ClientSecret"];
        var sender = config["Outlook:SenderEmail"];
        var adminEmail = config["Outlook:AdminEmail"] ?? "admin@adrover.in";
        if (new[] { tenantId, clientId, clientSecret, sender }.Any(string.IsNullOrWhiteSpace)
            || !MailAddress.TryCreate(adminEmail, out var admin))
        {
            return NotConfigured("email", "Outlook");
        }

        using var tokenResponse = await http.PostAsync(
            $"https://login.microsoftonline.com/{Uri.EscapeDataString(tenantId!)}/oauth2/v2.0/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId!,
                ["client_secret"] = clientSecret!,
                ["scope"] = "https://graph.microsoft.com/.default",
                ["grant_type"] = "client_credentials",
            }),
            cancellationToken);
        var tokenBody = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
        var accessToken = tokenResponse.IsSuccessStatusCode ? ReadString(tokenBody, "access_token") : null;
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return ProviderFailed("email", "Outlook authentication", tokenResponse, tokenBody);
        }

        var replyTo = MailAddress.TryCreate(request.ReplyTo, out var replyAddress)
            ? new[] { new { emailAddress = new { address = replyAddress.Address } } }
            : null;
        using var mail = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.microsoft.com/v1.0/users/{Uri.EscapeDataString(sender!)}/sendMail")
        {
            Content = Json(new
            {
                message = new
                {
                    subject = string.IsNullOrWhiteSpace(request.Subject) ? "New Adrover enquiry" : request.Subject.Trim(),
                    body = new { contentType = request.IsHtml ? "HTML" : "Text", content = request.Message },
                    toRecipients = new[] { new { emailAddress = new { address = admin.Address } } },
                    replyTo,
                },
                saveToSentItems = true,
            }),
        };
        mail.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(mail, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? new ChannelResult("email", true, admin.Address)
            : ProviderFailed("email", "Outlook", response, body);
    }

    async Task<ChannelResult> SendWhatsApp()
    {
        var token = config["WhatsApp:AccessToken"];
        var phoneNumberId = config["WhatsApp:PhoneNumberId"];
        var version = config["WhatsApp:ApiVersion"] ?? "v22.0";
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(phoneNumberId))
        {
            return NotConfigured("whatsapp", "WhatsApp");
        }

        object payload = string.IsNullOrWhiteSpace(request.TemplateName)
            ? new
            {
                messaging_product = "whatsapp",
                to = phone,
                type = "text",
                text = new { preview_url = false, body = request.Message },
            }
            : new
            {
                messaging_product = "whatsapp",
                to = phone,
                type = "template",
                template = new
                {
                    name = request.TemplateName.Trim(),
                    language = new { code = string.IsNullOrWhiteSpace(request.LanguageCode) ? "en" : request.LanguageCode },
                    components = request.Parameters is { Length: > 0 }
                        ? new[]
                        {
                            new
                            {
                                type = "body",
                                parameters = request.Parameters.Select(text => new { type = "text", text }).ToArray(),
                            },
                        }
                        : null,
                },
            };

        using var message = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://graph.facebook.com/{Uri.EscapeDataString(version)}/{Uri.EscapeDataString(phoneNumberId)}/messages")
        {
            Content = Json(payload),
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode
            ? new ChannelResult("whatsapp", true, phone, ReadString(body, "messages", 0, "id"))
            : ProviderFailed("whatsapp", "WhatsApp", response, body);
    }

    async Task<ChannelResult> SendSms()
    {
        var authKey = config["Msg91:AuthKey"];
        var templateId = config["Msg91:SmsTemplateId"];
        if (string.IsNullOrWhiteSpace(authKey) || string.IsNullOrWhiteSpace(templateId))
        {
            return NotConfigured("sms", "SMS");
        }

        var recipient = new Dictionary<string, string> { ["mobiles"] = phone };
        foreach (var (key, value) in request.Variables ?? [])
        {
            recipient[key] = value;
        }
        if (recipient.Count == 1 && !string.IsNullOrWhiteSpace(request.Message))
        {
            recipient[config["Msg91:MessageVariable"] ?? "message"] = request.Message;
        }

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://control.msg91.com/api/v5/flow")
        {
            Content = Json(new { template_id = templateId, short_url = "0", recipients = new[] { recipient } }),
        };
        message.Headers.Add("authkey", authKey);
        using var response = await http.SendAsync(message, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return response.IsSuccessStatusCode && ReadString(body, "type") == "success"
            ? new ChannelResult("sms", true, phone, ReadString(body, "message"))
            : ProviderFailed("sms", "SMS", response, body);
    }

    ChannelResult ProviderFailed(string channel, string provider, HttpResponseMessage response, string body)
    {
        logger.LogWarning(
            "{Provider} rejected the request with HTTP {StatusCode}: {Body}",
            provider,
            (int)response.StatusCode,
            body.Length > 500 ? body[..500] : body);
        return new ChannelResult(channel, false, Error: $"{provider} could not send the message.");
    }
});

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));
app.Run();

static StringContent Json(object value) =>
    new(JsonSerializer.Serialize(value, new JsonSerializerOptions
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    }), Encoding.UTF8, "application/json");

static ChannelResult NotConfigured(string channel, string provider) =>
    new(channel, false, Error: $"{provider} is not configured.");

static bool TryPhone(string? value, out string phone)
{
    var digits = Regex.Replace(value ?? "", "[\\s()-]", "");
    if (digits.StartsWith('+'))
    {
        digits = digits[1..];
    }
    if (Regex.IsMatch(digits, "^[6-9][0-9]{9}$"))
    {
        digits = $"91{digits}";
    }
    phone = digits;
    return Regex.IsMatch(digits, "^[1-9][0-9]{9,14}$");
}

static bool FixedEquals(string supplied, string expected) =>
    CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(supplied)),
        SHA256.HashData(Encoding.UTF8.GetBytes(expected)));

static string? ReadString(string json, params object[] path)
{
    try
    {
        using var document = JsonDocument.Parse(json);
        var element = document.RootElement;
        foreach (var segment in path)
        {
            if (segment is string name && element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var child))
            {
                element = child;
            }
            else if (segment is int index && element.ValueKind == JsonValueKind.Array && element.GetArrayLength() > index)
            {
                element = element[index];
            }
            else
            {
                return null;
            }
        }
        return element.ValueKind == JsonValueKind.String ? element.GetString() : null;
    }
    catch (JsonException)
    {
        return null;
    }
}

record SendMessageRequest(
    string[]? Channels,
    string? To,
    string? Subject,
    string? Message,
    string? ReplyTo,
    bool IsHtml,
    string? TemplateName,
    string? LanguageCode,
    string[]? Parameters,
    Dictionary<string, string>? Variables);

record ChannelResult(string Channel, bool Sent, string? To = null, string? ProviderMessageId = null, string? Error = null);
