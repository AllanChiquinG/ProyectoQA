using System.Net;
using MediCore.Contracts;
using MediCore.Contracts.Auth;
using MediCore.Contracts.Scheduling;
using MediCore.Mobile.Core.Api;

namespace MediCore.Mobile.Tests;

/// <summary>Lógica del cliente HTTP (renovación de tokens, errores, almacenamiento de sesión) con respuestas programadas; sin red.</summary>
public class ApiClientTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 13, 0, 0, TimeSpan.Zero);

    private static LoginResponse Login(string role = Roles.Paciente, string access = "acceso-1", string refresh = "refresco-1", int seconds = 900)
        => new(access, seconds, refresh, new UserDto(Guid.NewGuid(), "Ana Sofía García", "ana@example.com", role));

    private static (MobileApiClient Client, ScriptedHandler Handler, InMemorySessionStore Store, SessionEvents Events, FixedTime Time) Create(Func<HttpRequestMessage, int, HttpResponseMessage> respond)
    {
        var handler = new ScriptedHandler(respond);
        var store = new InMemorySessionStore();
        var events = new SessionEvents();
        var time = new FixedTime(Now);
        var client = new MobileApiClient(new HttpClient(handler) { BaseAddress = new Uri("http://api.test/") }, store, time, events);
        return (client, handler, store, events, time);
    }

    private static HttpResponseMessage Ok<T>(T body) => ScriptedHandler.Json(HttpStatusCode.OK, body);

    [Fact]
    public async Task Login_valido_guarda_los_tokens_pero_nunca_la_contrasena()
    {
        var (client, _, store, _, _) = Create((_, _) => Ok(Login()));

        var result = await client.LoginAsync("ana@example.com", "Contraseña-Secreta-1!");

        Assert.True(result.IsOk);
        var session = (await store.LoadAsync())!;
        Assert.Equal("acceso-1", session.AccessToken);
        Assert.DoesNotContain("Contraseña-Secreta-1!", System.Text.Json.JsonSerializer.Serialize(session));
    }

    [Fact]
    public async Task Login_con_credenciales_incorrectas_no_guarda_sesion()
    {
        var (client, _, store, _, _) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        var result = await client.LoginAsync("ana@example.com", "mala");

        Assert.Equal(ApiStatus.InvalidCredentials, result.Status);
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task Login_de_un_usuario_que_no_es_paciente_se_rechaza_y_no_se_guarda()
    {
        var (client, handler, store, _, _) = Create((request, _) => request.RequestUri!.AbsolutePath.EndsWith("logout") ? new HttpResponseMessage(HttpStatusCode.NoContent) : Ok(Login(Roles.Medico)));

        var result = await client.LoginAsync("medico@example.com", "x");

        Assert.Equal(ApiStatus.Forbidden, result.Status);
        Assert.Equal("Esta aplicación es solo para pacientes.", result.Message);
        Assert.Null(await store.LoadAsync());
        Assert.Contains(handler.Requests, r => r.Path.EndsWith("api/auth/logout")); // el token emitido se revoca
    }

    [Fact]
    public async Task Sin_conexion_el_login_devuelve_Offline()
    {
        var (client, _, _, _, _) = Create((_, _) => throw new HttpRequestException("sin red"));

        Assert.Equal(ApiStatus.Offline, (await client.LoginAsync("ana@example.com", "x")).Status);
    }

    [Fact]
    public async Task Limite_de_intentos_se_informa_como_RateLimited()
    {
        var (client, _, _, _, _) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        Assert.Equal(ApiStatus.RateLimited, (await client.LoginAsync("ana@example.com", "x")).Status);
    }

    [Fact]
    public async Task Sin_sesion_las_llamadas_devuelven_SessionExpired_sin_tocar_la_red()
    {
        var (client, handler, _, _, _) = Create((_, _) => Ok(new List<AppointmentDto>()));

        var result = await client.GetMyAppointmentsAsync();

        Assert.Equal(ApiStatus.SessionExpired, result.Status);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Una_llamada_autenticada_envia_el_token_de_acceso()
    {
        var (client, handler, store, _, time) = Create((_, _) => Ok(new List<AppointmentDto>()));
        await store.SaveAsync(new StoredSession("acceso-1", "refresco-1", time.Now.UtcDateTime.AddMinutes(10), "Ana"));

        var result = await client.GetMyAppointmentsAsync();

        Assert.True(result.IsOk);
        Assert.Equal("Bearer acceso-1", handler.Requests.Single().Authorization);
    }

    [Fact]
    public async Task Ante_un_401_renueva_una_vez_y_repite_la_llamada_con_el_token_nuevo()
    {
        var (client, handler, store, _, time) = Create((request, _) => request.RequestUri!.AbsolutePath.EndsWith("refresh")
            ? Ok(new RefreshResponse("acceso-2", 900, "refresco-2"))
            : request.Headers.Authorization!.Parameter == "acceso-2" ? Ok(new List<AppointmentDto>()) : new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await store.SaveAsync(new StoredSession("acceso-1", "refresco-1", time.Now.UtcDateTime.AddMinutes(10), "Ana"));

        var result = await client.GetMyAppointmentsAsync();

        Assert.True(result.IsOk);
        Assert.Equal(["/api/appointments/me", "/api/auth/refresh", "/api/appointments/me"], handler.Requests.Select(r => r.Path));
        Assert.Equal("refresco-2", (await store.LoadAsync())!.RefreshToken); // el refresco rota y se guarda el nuevo
    }

    [Fact]
    public async Task Renueva_antes_de_que_venza_el_token_de_acceso()
    {
        var (client, handler, store, _, time) = Create((request, _) => request.RequestUri!.AbsolutePath.EndsWith("refresh")
            ? Ok(new RefreshResponse("acceso-2", 900, "refresco-2")) : Ok(new List<AppointmentDto>()));
        await store.SaveAsync(new StoredSession("acceso-1", "refresco-1", time.Now.UtcDateTime.AddSeconds(5), "Ana"));

        await client.GetMyAppointmentsAsync();

        Assert.Equal("/api/auth/refresh", handler.Requests[0].Path);
        Assert.Equal("Bearer acceso-2", handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task Si_el_refresco_falla_la_sesion_se_cierra_y_se_avisa()
    {
        var (client, _, store, events, time) = Create((_, _) => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await store.SaveAsync(new StoredSession("acceso-1", "refresco-1", time.Now.UtcDateTime.AddMinutes(10), "Ana"));
        var ended = 0;
        events.SessionEnded += () => ended++;

        var result = await client.GetMyAppointmentsAsync();

        Assert.Equal(ApiStatus.SessionExpired, result.Status);
        Assert.Null(await store.LoadAsync());
        Assert.Equal(1, ended);
    }

    [Fact]
    public async Task Llamadas_simultaneas_con_acceso_vencido_renuevan_una_sola_vez()
    {
        var refreshes = 0;
        var (client, _, store, _, time) = Create((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("refresh"))
            {
                Interlocked.Increment(ref refreshes);
                return Ok(new RefreshResponse("acceso-2", 900, "refresco-2"));
            }

            return Ok(new List<AppointmentDto>());
        });
        await store.SaveAsync(new StoredSession("acceso-1", "refresco-1", time.Now.UtcDateTime.AddSeconds(1), "Ana"));

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.GetMyAppointmentsAsync()));

        Assert.All(results, r => Assert.True(r.IsOk));
        Assert.Equal(1, refreshes); // el refresco es de un solo uso: renovar dos veces con el mismo token revocaría la sesión
    }

    [Fact]
    public async Task Sin_conexion_las_llamadas_devuelven_Offline_y_conservan_la_sesion()
    {
        var (client, _, store, _, time) = Create((_, _) => throw new HttpRequestException("sin red"));
        await store.SaveAsync(new StoredSession("acceso-1", "refresco-1", time.Now.UtcDateTime.AddMinutes(10), "Ana"));

        var result = await client.GetMyAppointmentsAsync();

        Assert.Equal(ApiStatus.Offline, result.Status);
        Assert.NotNull(await store.LoadAsync());
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, ApiStatus.NotFound)]
    [InlineData(HttpStatusCode.Forbidden, ApiStatus.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, ApiStatus.Conflict)]
    [InlineData(HttpStatusCode.BadRequest, ApiStatus.Invalid)]
    [InlineData(HttpStatusCode.InternalServerError, ApiStatus.Unavailable)]
    public async Task Los_codigos_de_error_se_traducen_a_estados(HttpStatusCode code, ApiStatus expected)
    {
        var (client, _, store, _, time) = Create((_, _) => new HttpResponseMessage(code));
        await store.SaveAsync(new StoredSession("acceso-1", "refresco-1", time.Now.UtcDateTime.AddMinutes(10), "Ana"));

        Assert.Equal(expected, (await client.GetMyAppointmentsAsync()).Status);
    }

    [Fact]
    public async Task La_reserva_envia_la_clave_de_idempotencia_y_el_mensaje_de_negocio_se_conserva()
    {
        var (client, handler, store, _, time) = Create((_, _) => ScriptedHandler.Json(HttpStatusCode.Conflict,
            new { title = "Horario ocupado", detail = "Este horario ya está ocupado. Selecciona otro para continuar." }));
        await store.SaveAsync(new StoredSession("acceso-1", "refresco-1", time.Now.UtcDateTime.AddMinutes(10), "Ana"));

        var result = await client.CreateAppointmentAsync(new CreateAppointmentRequest(null, Guid.NewGuid(), new DateOnly(2026, 10, 20), "08:00"), "clave-12345678");

        Assert.Equal(ApiStatus.Conflict, result.Status);
        Assert.Equal("Este horario ya está ocupado. Selecciona otro para continuar.", result.Message);
        Assert.Equal("clave-12345678", handler.Requests.Single().Key);
    }

    [Fact]
    public async Task Cerrar_sesion_borra_la_sesion_local_aunque_la_API_no_responda()
    {
        var (client, _, store, _, time) = Create((_, _) => throw new HttpRequestException("sin red"));
        await store.SaveAsync(new StoredSession("acceso-1", "refresco-1", time.Now.UtcDateTime.AddMinutes(10), "Ana"));

        await client.LogoutAsync();

        Assert.Null(await store.LoadAsync());
        Assert.False(await client.HasSessionAsync());
    }
}
