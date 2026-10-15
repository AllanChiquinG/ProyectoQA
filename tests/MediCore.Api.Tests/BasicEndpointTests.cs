using Microsoft.AspNetCore.Hosting;
using System.Net;
using MediCore.Api.Tests.Infrastructure;

namespace MediCore.Api.Tests;

public class BasicEndpointTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Get_Health_es_publico_y_devuelve_200()
    {
        var response = await factory.CreateClient().GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("\"status\":\"ok\"", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ruta_inexistente_sin_token_devuelve_401_como_problem_details()
    {
        // Seguro por defecto: lo que no está marcado como público exige autenticación.
        var response = await factory.CreateClient().GetAsync("/api/no-existe");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Ready_devuelve_503_si_no_hay_cadena_de_conexion()
    {
        var response = await factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Postgres", "")).CreateClient()
            .GetAsync("/api/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Endpoints_protegidos_sin_token_devuelven_401()
    {
        var client = factory.CreateClient();

        foreach (var path in new[] { "/api/auth/me", "/api/users", "/api/audit" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }
}
