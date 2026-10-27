using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediCore.Api.Tests.Infrastructure;
using MediCore.Contracts.Doctors;
using MediCore.Domain.Identity;
using MediCore.Infrastructure.Caching;
using MediCore.Infrastructure.Identity;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace MediCore.Api.Tests;

/// <summary>Médicos: catálogo, permisos y cache Redis (HIT, MISS, TTL, invalidación, Redis no disponible).</summary>
[Trait("Category", "Integration")]
[Collection(StackCollection.Name)]
public class DoctorsApiTests(StackFixture stack)
{
    private static DoctorRequest NewRequest(string? name = null) =>
        new(name ?? $"Dr. {Guid.NewGuid():N}"[..14], "Pediatría", "5550 9999", "doctor.nuevo@example.com");

    private IDatabase Redis() => ConnectionMultiplexer.Connect(stack.RedisConnectionString).GetDatabase();

    private async Task ResetCacheAsync() => await Redis().KeyDeleteAsync(CacheKeys.DoctorCatalog);

    [Fact]
    public async Task Catalogo_requiere_sesion_y_el_personal_ve_los_datos_de_contacto()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await stack.CreateClient().GetAsync("/api/doctors")).StatusCode);

        var doctor = await stack.CreateDoctorAsync("Dr. Contacto Visible");
        await ResetCacheAsync(); // la prueba inserta directo en la BD: se descarta el catálogo cacheado por otras pruebas
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var list = await client.GetFromJsonAsync<List<DoctorDto>>("/api/doctors?q=Contacto Visible");

        var found = Assert.Single(list!);
        Assert.Equal(doctor.Id, found.Id);
        Assert.Equal("doctor@example.com", found.Email);
        Assert.Equal("5550 1111", found.Phone);
    }

    [Fact]
    public async Task Paciente_ve_nombre_y_especialidad_pero_no_el_contacto_del_medico()
    {
        await stack.CreateDoctorAsync("Dr. Sin Contacto");
        await ResetCacheAsync();
        var (client, _) = await stack.LoginClientAsync(UserRole.Paciente);

        var list = await client.GetFromJsonAsync<List<DoctorDto>>("/api/doctors?q=Sin Contacto");

        var found = Assert.Single(list!);
        Assert.Null(found.Email);
        Assert.Null(found.Phone);
        Assert.Equal("Cardiología", found.Specialty);
    }

    [Fact]
    public async Task Solo_el_administrador_crea_y_modifica_medicos()
    {
        var (recepcion, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);
        var request = NewRequest();

        Assert.Equal(HttpStatusCode.Forbidden, (await recepcion.PostAsJsonAsync("/api/doctors", request)).StatusCode);

        var created = await admin.PostAsJsonAsync("/api/doctors", request);
        var doctor = (await created.Content.ReadFromJsonAsync<DoctorDto>())!;
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        Assert.Equal(HttpStatusCode.Forbidden, (await recepcion.PutAsJsonAsync($"/api/doctors/{doctor.Id}", request)).StatusCode);
        var updated = await admin.PutAsJsonAsync($"/api/doctors/{doctor.Id}", request with { Specialty = "Neurología" });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Neurología", (await admin.GetFromJsonAsync<DoctorDto>($"/api/doctors/{doctor.Id}"))!.Specialty);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/doctors/{Guid.NewGuid()}", request)).StatusCode);
    }

    [Fact]
    public async Task Medico_consulta_su_propio_registro_y_sin_vinculo_recibe_404()
    {
        var (userId, email) = await stack.CreateUserAsync(UserRole.Medico);
        var doctor = await stack.CreateScheduledDoctorAsync(userId);
        var (client, _) = await stack.LoginClientForAsync(email);
        var (unlinked, _) = await stack.LoginClientAsync(UserRole.Medico);
        var (patient, _) = await stack.LoginClientAsync(UserRole.Paciente);

        Assert.Equal(doctor.Id, (await client.GetFromJsonAsync<DoctorDto>("/api/doctors/me"))!.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await unlinked.GetAsync("/api/doctors/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.GetAsync("/api/doctors/me")).StatusCode);
    }

    [Fact]
    public async Task Medico_con_datos_invalidos_devuelve_400()
    {
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);

        var response = await admin.PostAsJsonAsync("/api/doctors", new DoctorRequest("", "Pediatría", "x", "no-es-correo"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Cache_MISS_puebla_Redis_con_TTL_y_el_siguiente_acceso_es_HIT()
    {
        await ResetCacheAsync();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        Assert.False(await Redis().KeyExistsAsync(CacheKeys.DoctorCatalog));

        await client.GetAsync("/api/doctors"); // MISS

        Assert.True(await Redis().KeyExistsAsync(CacheKeys.DoctorCatalog));
        var ttl = await Redis().KeyTimeToLiveAsync(CacheKeys.DoctorCatalog);
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(5));

        // HIT: un cambio hecho directo en la base de datos (sin pasar por la API) no se ve mientras el dato esté cacheado.
        var hiddenName = $"Dr. Oculto {Guid.NewGuid():N}"[..20];
        await stack.CreateDoctorAsync(hiddenName);
        var cached = await client.GetFromJsonAsync<List<DoctorDto>>($"/api/doctors?q={hiddenName}");
        Assert.Empty(cached!);
    }

    [Fact]
    public async Task Cache_se_invalida_al_crear_o_modificar_un_medico_por_la_API()
    {
        var (client, _) = await stack.LoginClientAsync(UserRole.Administrador);
        await client.GetAsync("/api/doctors"); // puebla el cache
        Assert.True(await Redis().KeyExistsAsync(CacheKeys.DoctorCatalog));

        var created = await client.PostAsJsonAsync("/api/doctors", NewRequest("Dr. Invalidacion Uno"));
        var doctor = (await created.Content.ReadFromJsonAsync<DoctorDto>())!;
        Assert.False(await Redis().KeyExistsAsync(CacheKeys.DoctorCatalog));
        Assert.Single((await client.GetFromJsonAsync<List<DoctorDto>>("/api/doctors?q=Invalidacion Uno"))!);

        await client.PutAsJsonAsync($"/api/doctors/{doctor.Id}", NewRequest("Dr. Invalidacion Dos"));
        Assert.False(await Redis().KeyExistsAsync(CacheKeys.DoctorCatalog));
        Assert.Empty((await client.GetFromJsonAsync<List<DoctorDto>>("/api/doctors?q=Invalidacion Uno"))!);
        Assert.Single((await client.GetFromJsonAsync<List<DoctorDto>>("/api/doctors?q=Invalidacion Dos"))!);
    }

    [Fact]
    public async Task Dato_vencido_el_cache_expira_por_TTL_y_se_recarga_desde_la_base_de_datos()
    {
        await ResetCacheAsync();
        var shortTtl = stack.WithWebHostBuilder(b => b.UseSetting("Cache:DoctorCatalogTtl", "00:00:01"));
        var (_, email) = await stack.CreateUserAsync(UserRole.Recepcion);
        var client = shortTtl.CreateClient();
        var login = await StackFixture.LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        await client.GetAsync("/api/doctors"); // MISS: se cachea con TTL de 1 s
        var name = $"Dr. Tardio {Guid.NewGuid():N}"[..20];
        await stack.CreateDoctorAsync(name);
        Assert.Empty((await client.GetFromJsonAsync<List<DoctorDto>>($"/api/doctors?q={name}"))!); // HIT: dato anterior

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.False(await Redis().KeyExistsAsync(CacheKeys.DoctorCatalog));
        Assert.Single((await client.GetFromJsonAsync<List<DoctorDto>>($"/api/doctors?q={name}"))!); // MISS: dato fresco
    }

    [Fact]
    public async Task Si_Redis_no_esta_disponible_el_catalogo_se_sirve_desde_la_base_de_datos()
    {
        var doctor = await stack.CreateDoctorAsync($"Dr. Resiliente {Guid.NewGuid():N}"[..22]);
        var (_, login) = await stack.LoginClientAsync(UserRole.Recepcion);

        // Redis inalcanzable solo para el cache; la verificación de sesión se sustituye para aislar el comportamiento del cache.
        var degraded = stack.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false,connectTimeout=500");
            b.ConfigureTestServices(services => services.AddSingleton<ISessionStore>(new AlwaysValidSessionStore()));
        }).CreateClient();
        degraded.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var response = await degraded.GetAsync($"/api/doctors?q={doctor.FullName}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single((await response.Content.ReadFromJsonAsync<List<DoctorDto>>())!);
    }

    private sealed class AlwaysValidSessionStore : ISessionStore
    {
        public Task<(string SessionId, string RefreshToken)> CreateAsync(Guid userId, TimeSpan lifetime, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<RefreshResult> RotateAsync(string refreshToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeAsync(string sessionId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
