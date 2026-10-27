using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediCore.Api.Tests.Infrastructure;
using MediCore.Contracts.Scheduling;
using MediCore.Domain.Identity;
using MediCore.Infrastructure.Caching;
using MediCore.Infrastructure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using static MediCore.Api.Tests.Infrastructure.SchedulingHelpers;

namespace MediCore.Api.Tests;

/// <summary>Horario de atención (WEB-09), disponibilidad por fecha y su cache en Redis.</summary>
[Trait("Category", "Integration")]
[Collection(StackCollection.Name)]
public class AvailabilityApiTests(StackFixture stack)
{
    private IDatabase Redis() => ConnectionMultiplexer.Connect(stack.RedisConnectionString).GetDatabase();

    private static string Url(Guid doctorId, DateOnly date) => $"/api/doctors/{doctorId}/availability?date={date:yyyy-MM-dd}";

    // ---- Horario de atención ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Administrador_configura_y_consulta_el_horario_de_atencion_con_auditoria()
    {
        var doctor = await stack.CreateDoctorAsync();
        var (admin, login) = await stack.LoginClientAsync(UserRole.Administrador);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/doctors/{doctor.Id}/schedule")).StatusCode);

        var put = await admin.PutAsJsonAsync($"/api/doctors/{doctor.Id}/schedule", new ScheduleRequest([1, 3, 5], "08:00", "12:00"));
        var schedule = await admin.GetFromJsonAsync<ScheduleDto>($"/api/doctors/{doctor.Id}/schedule");

        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal([1, 3, 5], schedule!.Days);
        Assert.Equal(("08:00", "12:00", 30), (schedule.StartTime, schedule.EndTime, schedule.SlotMinutes));

        var again = await admin.PutAsJsonAsync($"/api/doctors/{doctor.Id}/schedule", new ScheduleRequest([2, 4], "09:00", "13:30"));
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        Assert.Equal("13:30", (await admin.GetFromJsonAsync<ScheduleDto>($"/api/doctors/{doctor.Id}/schedule"))!.EndTime);

        var audit = await admin.GetFromJsonAsync<List<MediCore.Contracts.Audit.AuditEntryDto>>("/api/audit?take=500");
        Assert.Equal(2, audit!.Count(e => e.Action == "schedules.updated" && e.ActorUserId == login.User.Id && e.ResourceId == doctor.Id.ToString()));
    }

    [Theory]
    [InlineData("12:00", "08:00")]   // fin antes del inicio
    [InlineData("08:00", "08:00")]   // sin duración
    [InlineData("08:15", "12:00")]   // inicio fuera de la cuadrícula de 30 min
    [InlineData("08:00", "12:10")]   // fin fuera de la cuadrícula
    [InlineData("8:00", "12:00")]    // formato incorrecto
    [InlineData("08:00", "24:00")]   // hora inexistente
    public async Task Horario_con_horas_invalidas_devuelve_400(string start, string end)
    {
        var doctor = await stack.CreateDoctorAsync();
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);

        var response = await admin.PutAsJsonAsync($"/api/doctors/{doctor.Id}/schedule", new ScheduleRequest([1, 2], start, end));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/doctors/{doctor.Id}/schedule")).StatusCode);
    }

    [Theory]
    [InlineData(new int[0])]
    [InlineData(new[] { 7 })]
    [InlineData(new[] { -1, 2 })]
    public async Task Horario_con_dias_invalidos_devuelve_400(int[] days)
    {
        var doctor = await stack.CreateDoctorAsync();
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);

        var response = await admin.PutAsJsonAsync($"/api/doctors/{doctor.Id}/schedule", new ScheduleRequest(days, "08:00", "12:00"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Solo_el_administrador_configura_el_horario_y_el_medico_debe_existir()
    {
        var doctor = await stack.CreateDoctorAsync();
        var request = new ScheduleRequest([1], "08:00", "12:00");
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);

        foreach (var role in new[] { UserRole.Recepcion, UserRole.Medico, UserRole.Paciente, UserRole.Laboratorio })
        {
            var (client, _) = await stack.LoginClientAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/doctors/{doctor.Id}/schedule", request)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/doctors/{doctor.Id}/schedule")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await stack.CreateClient().PutAsJsonAsync($"/api/doctors/{doctor.Id}/schedule", request)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/doctors/{Guid.NewGuid()}/schedule", request)).StatusCode);
    }

    // ---- Disponibilidad ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Disponibilidad_devuelve_los_horarios_de_30_minutos_del_dia_de_atencion()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var (client, _) = await stack.LoginClientAsync(UserRole.Paciente);

        var day = await client.GetFromJsonAsync<AvailabilityDto>(Url(doctor.Id, StackFixture.Future()));

        Assert.True(day!.IsWorkingDay);
        Assert.Equal(["08:00", "08:30", "09:00", "09:30", "10:00", "10:30", "11:00", "11:30"], day.Slots.Select(s => s.Time));
        Assert.All(day.Slots, s => Assert.True(s.Available));
    }

    [Fact]
    public async Task Dia_sin_atencion_o_medico_sin_horario_no_tiene_horarios()
    {
        var withoutSchedule = await stack.CreateDoctorAsync();
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);
        var date = StackFixture.Future(6);
        var doctor = await stack.CreateScheduledDoctorAsync();
        await admin.PutAsJsonAsync($"/api/doctors/{doctor.Id}/schedule",
            new ScheduleRequest(Enum.GetValues<DayOfWeek>().Where(d => d != date.DayOfWeek).Select(d => (int)d).ToArray(), "08:00", "12:00"));

        var closedDay = await admin.GetFromJsonAsync<AvailabilityDto>(Url(doctor.Id, date));
        var noSchedule = await admin.GetFromJsonAsync<AvailabilityDto>(Url(withoutSchedule.Id, date));

        Assert.False(closedDay!.IsWorkingDay);
        Assert.Empty(closedDay.Slots);
        Assert.False(noSchedule!.IsWorkingDay);
        Assert.Empty(noSchedule.Slots);
    }

    [Fact]
    public async Task Disponibilidad_con_fecha_invalida_o_medico_inexistente_devuelve_el_error_correcto()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/doctors/{doctor.Id}/availability")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/doctors/{doctor.Id}/availability?date=no-es-fecha")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/doctors/{doctor.Id}/availability?date=2026-02-30")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Url(doctor.Id, today.AddDays(-3)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(Url(doctor.Id, today.AddDays(400)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(Url(Guid.NewGuid(), StackFixture.Future()))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/doctors/no-es-guid/availability?date=2030-01-01")).StatusCode);
    }

    [Fact]
    public async Task Laboratorio_y_usuarios_sin_sesion_no_consultan_la_disponibilidad()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var (lab, _) = await stack.LoginClientAsync(UserRole.Laboratorio);

        Assert.Equal(HttpStatusCode.Forbidden, (await lab.GetAsync(Url(doctor.Id, StackFixture.Future()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await stack.CreateClient().GetAsync(Url(doctor.Id, StackFixture.Future()))).StatusCode);
    }

    // ---- Cache de disponibilidad (Redis) ----------------------------------------------------------------------------

    [Fact]
    public async Task Cache_MISS_puebla_Redis_con_TTL_y_el_siguiente_acceso_es_HIT()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var date = StackFixture.Future();
        var key = CacheKeys.Availability(doctor.Id, date);
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        Assert.False(await Redis().KeyExistsAsync(key));

        await client.GetAsync(Url(doctor.Id, date)); // MISS

        Assert.True(await Redis().KeyExistsAsync(key));
        Assert.InRange((await Redis().KeyTimeToLiveAsync(key))!.Value, TimeSpan.FromSeconds(60), TimeSpan.FromMinutes(2));

        // HIT: una cita insertada directo en la base de datos (sin pasar por la API) no se ve mientras el dato esté cacheado.
        var patient = await stack.CreatePatientAsync();
        await stack.CreateAppointmentAsync(patient.Id, doctor.Id, date.ToDateTime(new TimeOnly(9, 0)));
        var cached = await client.GetFromJsonAsync<AvailabilityDto>(Url(doctor.Id, date));
        Assert.True(cached!.Slots.Single(s => s.Time == "09:00").Available);
    }

    [Fact]
    public async Task Reservar_y_cancelar_invalidan_la_disponibilidad_cacheada()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var key = CacheKeys.Availability(doctor.Id, date);
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        await client.GetAsync(Url(doctor.Id, date));
        Assert.True(await Redis().KeyExistsAsync(key));

        var booked = await (await client.BookAsync(Request(patient.Id, doctor.Id, date, "10:00"))).ReadAppointmentAsync();
        Assert.False(await Redis().KeyExistsAsync(key));
        Assert.False((await client.GetFromJsonAsync<AvailabilityDto>(Url(doctor.Id, date)))!.Slots.Single(s => s.Time == "10:00").Available);

        Assert.True(await Redis().KeyExistsAsync(key));
        await client.PostAsync($"/api/appointments/{booked.Id}/cancel", null);
        Assert.False(await Redis().KeyExistsAsync(key));
        Assert.True((await client.GetFromJsonAsync<AvailabilityDto>(Url(doctor.Id, date)))!.Slots.Single(s => s.Time == "10:00").Available);
    }

    [Fact]
    public async Task Cambiar_el_horario_de_atencion_invalida_todas_las_fechas_cacheadas_del_medico()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var dates = new[] { StackFixture.Future(3), StackFixture.Future(4), StackFixture.Future(5) };
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);
        foreach (var date in dates) await admin.GetAsync(Url(doctor.Id, date));
        Assert.All(dates, d => Assert.True(Redis().KeyExists(CacheKeys.Availability(doctor.Id, d))));

        await admin.PutAsJsonAsync($"/api/doctors/{doctor.Id}/schedule", new ScheduleRequest([0, 1, 2, 3, 4, 5, 6], "08:00", "10:00"));

        Assert.All(dates, d => Assert.False(Redis().KeyExists(CacheKeys.Availability(doctor.Id, d))));
        var updated = await admin.GetFromJsonAsync<AvailabilityDto>(Url(doctor.Id, dates[0]));
        Assert.Equal(["08:00", "08:30", "09:00", "09:30"], updated!.Slots.Select(s => s.Time));
    }

    [Fact]
    public async Task Dato_vencido_el_cache_expira_por_TTL_y_se_recarga_desde_la_base_de_datos()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var shortTtl = stack.WithWebHostBuilder(b => b.UseSetting("Cache:AvailabilityTtl", "00:00:01"));
        var (_, email) = await stack.CreateUserAsync(UserRole.Recepcion);
        var client = shortTtl.CreateClient();
        var login = await StackFixture.LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        await client.GetAsync(Url(doctor.Id, date)); // MISS, TTL de 1 s
        await stack.CreateAppointmentAsync(patient.Id, doctor.Id, date.ToDateTime(new TimeOnly(9, 0)));
        Assert.True((await client.GetFromJsonAsync<AvailabilityDto>(Url(doctor.Id, date)))!.Slots.Single(s => s.Time == "09:00").Available); // HIT: dato anterior

        await Task.Delay(TimeSpan.FromSeconds(1.5));

        Assert.False(Redis().KeyExists(CacheKeys.Availability(doctor.Id, date)));
        Assert.False((await client.GetFromJsonAsync<AvailabilityDto>(Url(doctor.Id, date)))!.Slots.Single(s => s.Time == "09:00").Available); // MISS: dato fresco
    }

    [Fact]
    public async Task Si_Redis_no_esta_disponible_la_disponibilidad_y_la_reserva_siguen_funcionando()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var (_, login) = await stack.LoginClientAsync(UserRole.Recepcion);

        // Redis inalcanzable para el cache; la verificación de sesión se sustituye para aislar el comportamiento del cache.
        var degraded = stack.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false,connectTimeout=500");
            b.ConfigureTestServices(services => services.AddSingleton<ISessionStore>(new AlwaysValidSessionStore()));
        }).CreateClient();
        degraded.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var availability = await degraded.GetAsync(Url(doctor.Id, date));
        var booking = await degraded.BookAsync(Request(patient.Id, doctor.Id, date, "09:00"));
        var afterBooking = await degraded.GetFromJsonAsync<AvailabilityDto>(Url(doctor.Id, date));

        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        Assert.Equal(HttpStatusCode.Created, booking.StatusCode);
        Assert.False(afterBooking!.Slots.Single(s => s.Time == "09:00").Available);
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
