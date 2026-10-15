using Microsoft.AspNetCore.Mvc;

namespace MediCore.Api.Controllers;

[ApiController]
[Route("api/health")]
public class HealthController : ControllerBase
{
    /// <summary>Comprobación básica de que la API responde. No toca dependencias externas.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", service = "MediCore.Api" });
}
