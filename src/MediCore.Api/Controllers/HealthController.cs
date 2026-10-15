using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MediCore.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/health")]
public class HealthController : ControllerBase
{
    /// <summary>Liveness: la API responde. No toca dependencias externas.</summary>
    [HttpGet]
    public IActionResult Get() => Ok(new { status = "ok", service = "MediCore.Api" });

    /// <summary>Readiness: además de responder, puede hablar con PostgreSQL.</summary>
    [HttpGet("ready")]
    public async Task<IActionResult> Ready([FromServices] MediCoreDbContext db, CancellationToken ct)
    {
        var database = false;
        try
        {
            database = await db.Database.CanConnectAsync(ct);
        }
        catch (InvalidOperationException)
        {
            // Sin cadena de conexión configurada: se reporta como no disponible.
        }

        var body = new { status = database ? "ok" : "unavailable", checks = new { postgres = database ? "ok" : "unavailable" } };
        return database ? Ok(body) : StatusCode(StatusCodes.Status503ServiceUnavailable, body);
    }
}
