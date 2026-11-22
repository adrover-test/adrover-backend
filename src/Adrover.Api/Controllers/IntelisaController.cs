using Adrover.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace Adrover.Backend.Controllers;

[ApiController]
[Route("api/intelisa")]
public sealed class IntelisaController(IntelisaAuthService service) : ControllerBase
{
    [HttpPost("login")]
    public async Task<IActionResult> Login(CancellationToken cancellationToken)
    {
        var result = await service.LoginAsync(cancellationToken);
        return StatusCode(result.StatusCode, result.Body);
    }
}
