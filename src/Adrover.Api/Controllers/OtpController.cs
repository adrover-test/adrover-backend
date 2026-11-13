using Adrover.Backend.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.RegularExpressions;

namespace Adrover.Backend.Controllers;

[ApiController]
[Route("api/otp")]
public sealed class OtpController(OtpService service) : ControllerBase
{
    [HttpPost("send")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> Send(
        [FromBody] Dictionary<string, string?> fields, CancellationToken cancellationToken)
    {
        var mobile = fields.GetValueOrDefault("mobileNumber") ?? "";
        if (!Regex.IsMatch(mobile, "^[6-9][0-9]{9}\\z"))
        {
            return Problem("Enter a 10-digit Indian mobile number without +91.", statusCode: 400);
        }
        var result = await service.SendAsync(mobile, cancellationToken);
        return StatusCode(result.StatusCode, result.Body);
    }

    [HttpPost("verify")]
    [RequestSizeLimit(1024)]
    public async Task<IActionResult> Verify(
        [FromBody] Dictionary<string, string?> fields, CancellationToken cancellationToken)
    {
        var mobile = fields.GetValueOrDefault("mobileNumber") ?? "";
        var otp = fields.GetValueOrDefault("otp") ?? "";
        if (!Regex.IsMatch(mobile, "^[6-9][0-9]{9}\\z") || !Regex.IsMatch(otp, "^[0-9]{6}\\z"))
        {
            return Problem("Enter a 10-digit Indian mobile number and a 6-digit OTP.", statusCode: 400);
        }
        var result = await service.VerifyAsync(mobile, otp, cancellationToken);
        return StatusCode(result.StatusCode, result.Body);
    }
}
