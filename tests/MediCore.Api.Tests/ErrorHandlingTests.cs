using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MediCore.Api.Tests;

public class ErrorHandlingTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Ruta_inexistente_devuelve_404_como_problem_details()
    {
        var response = await factory.CreateClient().GetAsync("/api/no-existe");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Ready_devuelve_503_si_no_hay_cadena_de_conexion()
    {
        var response = await factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Postgres", "")).CreateClient()
            .GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }
}
