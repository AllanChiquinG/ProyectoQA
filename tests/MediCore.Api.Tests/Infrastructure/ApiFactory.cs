using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MediCore.Api.Tests.Infrastructure;

/// <summary>API sin dependencias externas. Usa el entorno "Testing" para no leer los user-secrets del desarrollador.</summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    // Clave de prueba, sin valor real.
    public const string TestSigningKey = "clave-de-prueba-solo-para-tests-0123456789-abcdef";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("RateLimiting:AuthPermitsPerMinute", "1000");
    }
}
