using Microsoft.AspNetCore.Hosting;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediCore.Api.Tests.Infrastructure;
using MediCore.Contracts;
using MediCore.Contracts.Audit;
using MediCore.Contracts.Auth;
using MediCore.Contracts.Users;
using MediCore.Domain.Identity;

namespace MediCore.Api.Tests;

/// <summary>Autenticación, sesiones en Redis, roles y auditoría contra PostgreSQL y Redis reales.</summary>
[Trait("Category", "Integration")]
[Collection(StackCollection.Name)]
public class IdentityApiTests(StackFixture stack)
{
    private static HttpClient Bearer(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Login_correcto_devuelve_tokens_y_datos_del_usuario()
    {
        var (_, email) = await stack.CreateUserAsync(UserRole.Medico);

        var login = await StackFixture.LoginAsync(stack.CreateClient(), email);

        Assert.False(string.IsNullOrEmpty(login.AccessToken));
        Assert.Contains('.', login.RefreshToken);
        Assert.True(login.ExpiresInSeconds > 0);
        Assert.Equal(Roles.Medico, login.User.Role);
        Assert.Equal(email, login.User.Email);
    }

    [Fact]
    public async Task Login_fallido_devuelve_el_mismo_401_para_correo_inexistente_y_contrasena_incorrecta()
    {
        var (_, email) = await stack.CreateUserAsync(UserRole.Recepcion);
        var client = stack.CreateClient();

        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "incorrecta"));
        var unknownUser = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("nadie@example.com", "incorrecta"));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownUser.StatusCode);
        var a = await wrongPassword.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        var b = await unknownUser.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>();
        Assert.Equal((a!.Title, a.Detail), (b!.Title, b.Detail));
    }

    [Fact]
    public async Task Login_con_datos_invalidos_devuelve_400()
    {
        var response = await stack.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest("no-es-un-correo", ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cuenta_se_bloquea_tras_cinco_intentos_fallidos_incluso_con_la_contrasena_correcta()
    {
        var (_, email) = await stack.CreateUserAsync(UserRole.Laboratorio);
        var client = stack.CreateClient();

        for (var i = 0; i < User.MaxFailedAttempts; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "incorrecta"))).StatusCode);

        var withCorrectPassword = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, StackFixture.Password));
        Assert.Equal(HttpStatusCode.Unauthorized, withCorrectPassword.StatusCode);
    }

    [Fact]
    public async Task Usuario_inactivo_no_puede_iniciar_sesion()
    {
        var (_, email) = await stack.CreateUserAsync(UserRole.Recepcion, active: false);

        var response = await stack.CreateClient().PostAsJsonAsync("/api/auth/login", new LoginRequest(email, StackFixture.Password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_con_token_valido_devuelve_el_usuario()
    {
        var (client, login) = await stack.LoginClientAsync(UserRole.Paciente);

        var me = await client.GetFromJsonAsync<UserDto>("/api/auth/me");

        Assert.Equal(login.User.Id, me!.Id);
    }

    [Fact]
    public async Task Token_manipulado_devuelve_401()
    {
        var (client, login) = await stack.LoginClientAsync(UserRole.Medico);
        var tampered = login.AccessToken[..^3] + (login.AccessToken.EndsWith("AAA") ? "BBB" : "AAA");

        var response = await Bearer(stack.CreateClient(), tampered).GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Token_vencido_devuelve_401()
    {
        var shortLived = stack.WithWebHostBuilder(b => b.UseSetting("Jwt:AccessTokenLifetime", "00:00:01"));
        var (_, email) = await stack.CreateUserAsync(UserRole.Medico);
        var client = shortLived.CreateClient();
        var login = await StackFixture.LoginAsync(client, email);
        Bearer(client, login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        await Task.Delay(TimeSpan.FromSeconds(2.5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_revoca_la_sesion_y_el_token_deja_de_servir()
    {
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Refresh_rota_el_token_y_entrega_un_acceso_valido()
    {
        var (_, email) = await stack.CreateUserAsync(UserRole.Medico);
        var client = stack.CreateClient();
        var login = await StackFixture.LoginAsync(client, email);

        var response = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken));
        var refreshed = (await response.Content.ReadFromJsonAsync<RefreshResponse>())!;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, (await Bearer(stack.CreateClient(), refreshed.AccessToken).GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Reutilizar_un_refresh_token_ya_rotado_revoca_toda_la_sesion()
    {
        var (_, email) = await stack.CreateUserAsync(UserRole.Medico);
        var client = stack.CreateClient();
        var login = await StackFixture.LoginAsync(client, email);
        var rotated = (await (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken)))
            .Content.ReadFromJsonAsync<RefreshResponse>())!;

        var reuse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken));
        var legitimateAfterReuse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(rotated.RefreshToken));

        Assert.Equal(HttpStatusCode.Unauthorized, reuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, legitimateAfterReuse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Bearer(stack.CreateClient(), rotated.AccessToken).GetAsync("/api/auth/me")).StatusCode);
    }

    [Theory]
    [InlineData("basura")]
    [InlineData("a.b")]
    public async Task Refresh_con_token_invalido_devuelve_401(string token)
    {
        var response = await stack.CreateClient().PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Recepcion)]
    [InlineData(UserRole.Medico)]
    [InlineData(UserRole.Laboratorio)]
    [InlineData(UserRole.Paciente)]
    public async Task Solo_el_administrador_puede_listar_usuarios_y_auditoria(UserRole role)
    {
        var (client, _) = await stack.LoginClientAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/audit")).StatusCode);
    }

    [Fact]
    public async Task Administrador_lista_usuarios_y_consulta_auditoria()
    {
        var (client, admin) = await stack.LoginClientAsync(UserRole.Administrador);

        var users = await client.GetFromJsonAsync<List<UserDto>>("/api/users");
        var audit = await client.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit");

        Assert.Contains(users!, u => u.Id == admin.User.Id);
        Assert.Contains(audit!, e => e.Action == "auth.login" && e.ActorUserId == admin.User.Id);
    }

    [Fact]
    public async Task Cambiar_el_rol_cierra_las_sesiones_del_usuario_y_queda_auditado()
    {
        var (adminClient, admin) = await stack.LoginClientAsync(UserRole.Administrador);
        var (targetClient, target) = await stack.LoginClientAsync(UserRole.Paciente);

        var response = await adminClient.PutAsJsonAsync($"/api/users/{target.User.Id}/role", new ChangeRoleRequest(Roles.Medico));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await targetClient.GetAsync("/api/auth/me")).StatusCode);
        var newLogin = await StackFixture.LoginAsync(stack.CreateClient(), target.User.Email);
        Assert.Equal(Roles.Medico, newLogin.User.Role);

        var audit = await adminClient.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit?take=200");
        Assert.Contains(audit!, e => e.Action == "users.role_changed" && e.ActorUserId == admin.User.Id
            && e.ResourceId == target.User.Id.ToString() && e.Details == "Paciente->Medico");
    }

    [Fact]
    public async Task El_administrador_no_puede_cambiar_su_propio_rol()
    {
        var (client, admin) = await stack.LoginClientAsync(UserRole.Administrador);

        var response = await client.PutAsJsonAsync($"/api/users/{admin.User.Id}/role", new ChangeRoleRequest(Roles.Recepcion));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Cambiar_a_un_rol_inexistente_devuelve_400()
    {
        var (client, _) = await stack.LoginClientAsync(UserRole.Administrador);
        var (_, target) = await stack.LoginClientAsync(UserRole.Recepcion);

        var response = await client.PutAsJsonAsync($"/api/users/{target.User.Id}/role", new ChangeRoleRequest("SuperAdmin"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_se_limita_por_ip()
    {
        var limited = stack.WithWebHostBuilder(b => b.UseSetting("RateLimiting:AuthPermitsPerMinute", "3")).CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 5; i++)
            statuses.Add((await limited.PostAsJsonAsync("/api/auth/login", new LoginRequest("nadie@example.com", "x"))).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[^1]);
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Unauthorized));
    }

    [Fact]
    public async Task Con_redis_caido_el_login_devuelve_503_en_lugar_de_un_500()
    {
        var (_, email) = await stack.CreateUserAsync(UserRole.Medico);
        var noRedis = stack.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false,connectTimeout=500")).CreateClient();

        var response = await noRedis.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, StackFixture.Password));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task Con_redis_caido_un_token_valido_deja_de_aceptarse()
    {
        var (_, login) = await stack.LoginClientAsync(UserRole.Medico);
        var noRedis = stack.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false,connectTimeout=500")).CreateClient();

        var response = await Bearer(noRedis, login.AccessToken).GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
