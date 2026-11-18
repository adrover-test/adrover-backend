using Adrover.Backend.Services;
using Adrover.Data.Context;
using Adrover.Domain.Entities;
using Microsoft.AspNetCore.Mvc;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace Adrover.Backend.Controllers;

[ApiController]
[Route("api/inquiries")]
public sealed class InquiryController(
    InquiryService service,
    AdRoverDbContext dbContext) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(16384)]
    public async Task<IActionResult> Send(
        [FromBody] Dictionary<string, string?> fields,
        CancellationToken cancellationToken)
    {
        if (fields.Count == 0)
        {
            return BadRequest(new { message = "Inquiry payload is required." });
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
        var normalizedPhone = InquiryService.NormalizePhone(inquiry["phone"]);
        var enquiry = new Enquiry
        {
            Id = Guid.NewGuid(),
            Name = inquiry["name"],
            Email = inquiry["email"],
            Phone = normalizedPhone ?? inquiry["phone"],
            Subject = inquiry["goal"],
            Message = string.Join(Environment.NewLine, new[]
            {
                $"InquiryId: {inquiryId}",
                $"Business: {inquiry["business"]}",
                $"Preferred format: {inquiry["format"]}",
                $"Additional details: {inquiry["message"]}"
            }),
            SourceChannel = "WebForm",
            Status = "New",
            CreatedAt = DateTime.UtcNow
        };

        dbContext.Enquiries.Add(enquiry);
        await dbContext.SaveChangesAsync(cancellationToken);

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
