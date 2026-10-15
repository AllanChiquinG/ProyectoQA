using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace MediCore.Api.Security;

/// <summary>Si Redis no responde, devuelve 503 en lugar de un 500 genérico. Sin la sesión no se puede autenticar: falla cerrado.</summary>
public sealed class DependencyUnavailableExceptionHandler(IProblemDetailsService problems, ILogger<DependencyUnavailableExceptionHandler> logger)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not (RedisException or RedisTimeoutException))
            return false;

        logger.LogError(exception, "Redis no disponible");
        httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Servicio no disponible",
                Detail = "No se pudo completar la operación. Inténtalo de nuevo en unos minutos.",
            },
        });
    }
}
