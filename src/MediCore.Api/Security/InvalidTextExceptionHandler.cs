using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace MediCore.Api.Security;

/// <summary>PostgreSQL rechaza caracteres como el byte nulo en un texto. Es un dato inválido del cliente: responde 400 y no 500.</summary>
public sealed class InvalidTextExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    private const string CharacterNotInRepertoire = "22021";

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var database = exception as PostgresException ?? exception.InnerException as PostgresException;
        if (database?.SqlState != CharacterNotInRepertoire)
            return false;

        httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
        return await problems.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Datos inválidos",
                Detail = "El texto enviado contiene caracteres no permitidos.",
            },
        });
    }
}
