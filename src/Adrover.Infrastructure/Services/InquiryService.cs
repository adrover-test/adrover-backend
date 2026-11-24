using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MimeKit;
using MailKit.Security;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Adrover.Backend.Services;

public sealed class InquiryService(
    IHttpClientFactory clients,
    IConfiguration configuration,
    IHostEnvironment environment,
    ILogger<InquiryService> logger)
{
    public sealed record DeliveryResult(string Status, string? Error);

    public async Task<DeliveryResult> SendClientMessageAsync(
        string channel, string recipient, string subject, string message)
    {
        if (configuration["Notifications:Mode"] == "Simulation")
            return new("Failed", "Notifications are in simulation mode. No client message was sent.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            var result = channel switch
            {
                "email" => await SendEmailMessageAsync(recipient, subject, message, null, timeout.Token),
                "sms" => await SendClientSmsAsync(recipient, message, timeout.Token),
                _ => new DeliveryResult("Failed", "Unsupported message channel.")
            };
            if (result.Status != "Accepted")
                logger.LogWarning("Client {Channel} follow-up: {Status}. {Error}", channel, result.Status, result.Error);
            return result;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or OperationCanceledException
            or MailKit.Net.Smtp.SmtpProtocolException or System.Net.Sockets.SocketException or IOException)
        {
            logger.LogWarning("Client {Channel} follow-up provider outcome is unknown: {FailureType}.",
                channel, ex.GetType().Name);
            return new("Unknown", "Provider acceptance is unknown. Check delivery before sending another message.");
        }
    }

    private async Task<DeliveryResult> SendClientSmsAsync(string phone, string message, CancellationToken ct)
    {
        var recipient = NormalizePhone(phone);
        var authKey = configuration["Msg91:AuthKey"];
        var template = configuration["Msg91:FollowUpTemplateId"];
        var variable = configuration["Msg91:FollowUpMessageVariable"];
        if (string.IsNullOrWhiteSpace(authKey) || string.IsNullOrWhiteSpace(template)
            || string.IsNullOrWhiteSpace(variable) || !Regex.IsMatch(variable, "^[A-Za-z][A-Za-z0-9_]*$")
            || variable.Equals("mobiles", StringComparison.OrdinalIgnoreCase)
            || recipient == null || !Regex.IsMatch(recipient, "^91[6-9][0-9]{9}$"))
            return new("Failed", "Configure MSG91 follow-up credentials, an approved message template/variable and a valid Indian client phone.");
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://control.msg91.com/api/v5/flow")
        {
            Content = JsonContent.Create(new
            {
                template_id = template, short_url = "0",
                recipients = new[] { new Dictionary<string, string> { ["mobiles"] = recipient, [variable] = message } }
            })
        };
        request.Headers.Add("authkey", authKey);
        using var response = await clients.CreateClient("notifications").SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            return (int)response.StatusCode >= 400 && (int)response.StatusCode < 500
                ? new("Failed", Failure("MSG91", response))
                : new("Unknown", "MSG91 acceptance is unknown. Check with the provider before resending.");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("type", out var type)
            && type.ValueKind == JsonValueKind.String)
        {
            if (type.GetString() == "success") return new("Accepted", null);
            if (type.GetString() == "error") return new("Failed", "MSG91 did not accept the SMS.");
        }
        return new("Unknown", "MSG91 returned an unrecognized response. Check with the provider before resending.");
    }

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
        var http = clients.CreateClient("notifications");
        foreach (var channel in new[] { "email", "whatsapp", "sms" })
        {
            try
            {
                var error = channel switch
                {
                    "email" => await SendEmailAsync(inquiry, inquiryId, cancellationToken),
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
        IReadOnlyDictionary<string, string> inquiry, string id, CancellationToken token)
    {
        var admin = configuration["Smtp:AdminEmail"];
        if (!MailAddress.TryCreate(admin, out var recipient))
            return "SMTP admin email is not configured correctly.";

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

        var result = await SendEmailMessageAsync(recipient.Address, $"Adrover inquiry - {inquiry["business"]}",
            body, inquiry["email"], token);
        return result.Error;
    }

    private async Task<DeliveryResult> SendEmailMessageAsync(
        string recipient, string subject, string body, string? replyTo, CancellationToken token)
    {
        var smtpHost = configuration["Smtp:Host"];
        var smtpUsername = configuration["Smtp:Username"];
        var smtpPassword = configuration["Smtp:Password"];
        var sender = configuration["Smtp:FromEmail"];
        if (string.IsNullOrWhiteSpace(smtpHost)
            || !int.TryParse(configuration["Smtp:Port"], out var smtpPort) || smtpPort <= 0 || smtpPort > 65535
            || string.IsNullOrWhiteSpace(smtpUsername) || string.IsNullOrWhiteSpace(smtpPassword)
            || !MailAddress.TryCreate(sender, out _) || !MailAddress.TryCreate(recipient, out _))
            return new("Failed", "SMTP credentials or client/sender email are not configured correctly.");
        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(configuration["Smtp:FromName"] ?? "Adrover", sender));
            message.To.Add(MailboxAddress.Parse(recipient));
            message.Subject = subject;
            if (replyTo != null) message.ReplyTo.Add(MailboxAddress.Parse(replyTo));
            message.Body = new TextPart("plain") { Text = body };

            using var smtp = new MailKit.Net.Smtp.SmtpClient();
            await smtp.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls, token);
            await smtp.AuthenticateAsync(smtpUsername, smtpPassword, token);
            await smtp.SendAsync(message, token);
            return new("Accepted", null);
        }
        catch (Exception ex) when (ex is MailKit.Net.Smtp.SmtpCommandException
            or MailKit.Security.AuthenticationException or MailKit.Security.SslHandshakeException
            or MailKit.ServiceNotAuthenticatedException or NotSupportedException)
        {
            logger.LogWarning("SMTP rejected a message: {FailureType}.", ex.GetType().Name);
            return new("Failed", "SMTP authentication or message submission was rejected.");
        }
        catch (Exception ex) when ((ex is MailKit.Net.Smtp.SmtpProtocolException
            or System.Net.Sockets.SocketException or IOException or OperationCanceledException)
            && !token.IsCancellationRequested)
        {
            logger.LogWarning("SMTP acceptance is unknown: {FailureType}.", ex.GetType().Name);
            return new("Unknown", "SMTP acceptance is unknown. Check delivery before resending.");
        }
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
