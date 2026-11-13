using Adrover.Backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Adrover.Backend.Controllers;

[ApiController]
[Route("api/inquiries")]
public sealed class InquiryController(InquiryService service, IConfiguration configuration) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Send(
        [FromBody] Dictionary<string, string?> fields,
        CancellationToken cancellationToken)
    {
        var expectedKey = configuration["Inquiry:ApiKey"];
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            return Problem("Inquiry API key is not configured.", statusCode: 503);
        }

        var suppliedKey = Request.Headers["X-Api-Key"].ToString();
        if (!CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(suppliedKey)),
            SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey))))
        {
            return Problem("A valid X-Api-Key header is required.", statusCode: 401);
        }

        string Value(string key) => fields.GetValueOrDefault(key)?.Trim() ?? "";
        var inquiry = new Dictionary<string, string>
        {
            ["name"] = Value("name"),
            ["business"] = Value("business"),
            ["email"] = Value("email"),
            ["phone"] = Value("phone"),
            ["goal"] = Value("goal"),
            ["format"] = Value("format"),
            ["message"] = Value("message")
        };
        foreach (var (key, maxLength) in new Dictionary<string, int>
        {
            ["name"] = 100, ["business"] = 150, ["email"] = 254,
            ["phone"] = 35, ["goal"] = 100, ["format"] = 50, ["message"] = 2000
        })
        {
            if (inquiry[key].Length > maxLength)
            {
                ModelState.AddModelError(key, $"Maximum length is {maxLength} characters.");
            }
        }
        foreach (var key in new[] { "name", "business", "email", "goal", "format" })
        {
            if (inquiry[key].Length == 0)
            {
                ModelState.AddModelError(key, "This field is required.");
            }
        }

        if (!MailAddress.TryCreate(inquiry["email"], out var address)
            || !string.Equals(address.Address, inquiry["email"], StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError("email", "Enter a valid work email address.");
        }
        if (inquiry["phone"].Length > 0 && InquiryService.NormalizePhone(inquiry["phone"]) is null)
        {
            ModelState.AddModelError("phone", "Enter an Indian mobile number or an international number with country code.");
        }
        if (!new[]
        {
            "Introduce my business", "Launch a product or service",
            "Promote an offer or event", "Discuss a campaign idea"
        }.Contains(inquiry["goal"]))
        {
            ModelState.AddModelError("goal", "Choose one of the campaign objectives shown in the form.");
        }
        if (!new[] { "Cabs", "Buses", "Both" }.Contains(inquiry["format"]))
        {
            ModelState.AddModelError("format", "Use Cabs, Buses or Both.");
        }
        foreach (var key in inquiry.Keys.Where(key => key != "message"))
        {
            if (Regex.IsMatch(inquiry[key], @"[\r\n\x00]"))
            {
                ModelState.AddModelError(key, "Line breaks and null characters are not allowed.");
            }
        }
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var inquiryId = Guid.NewGuid().ToString("N");
        var results = await service.SendAsync(inquiry, inquiryId, cancellationToken);
        var accepted = results.Count(result => result.Accepted);
        var simulated = results.All(result => result.Simulated);
        return StatusCode(simulated || accepted == 3 ? 200 : accepted == 0 ? 502 : 207, new
        {
            inquiryId,
            status = simulated ? "simulated" : accepted == 3 ? "accepted" : accepted == 0 ? "failed" : "partial",
            results = results.Select(result => new
            {
                channel = result.Channel,
                accepted = result.Accepted,
                simulated = result.Simulated,
                error = result.Error
            })
        });
    }
}
