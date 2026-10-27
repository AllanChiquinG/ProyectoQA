using System.Net.Http.Json;
using MediCore.Api.Tests.Infrastructure;
using MediCore.Contracts.Clinical;
using MediCore.Contracts.Laboratory;
using MediCore.Contracts.Scheduling;
using MediCore.Domain.Identity;
using MediCore.Domain.Notifications;
using MediCore.Infrastructure.Persistence;
using MediCore.Mobile.Core.Api;
using Microsoft.Extensions.DependencyInjection;
using static MediCore.Api.Tests.Infrastructure.SchedulingHelpers;

namespace MediCore.Api.Tests;

/// <summary>
/// El cliente de la app móvil (<see cref="MobileApiClient"/>) contra la API REAL (PostgreSQL y Redis reales, sin respuestas simuladas): inicio de sesión,
/// renovación y cierre de sesión, citas, reservas sin duplicados, resultados, recetas, notificaciones y autorización por recurso.
/// </summary>
[Trait("Category", "Integration")]
[Collection(StackCollection.Name)]
public class MobileApiIntegrationTests(StackFixture stack)
{
    private sealed record MobilePatient(MobileApiClient Client, InMemorySessionStore Store, SessionEvents Events, Domain.Patients.Patient Patient, string Email);

    private MobileApiClient NewClient(out InMemorySessionStore store, out SessionEvents events, TimeProvider? time = null)
    {
        store = new InMemorySessionStore();
        events = new SessionEvents();
        return new MobileApiClient(stack.CreateClient(), store, time ?? TimeProvider.System, events);
    }

    private async Task<MobilePatient> NewPatientAsync(string name = "Ana Sofía García")
    {
        var (userId, email) = await stack.CreateUserAsync(UserRole.Paciente);
        var patient = await stack.CreatePatientAsync(userId, name: name);
        var client = NewClient(out var store, out var events);
        var login = await client.LoginAsync(email, StackFixture.Password);
        Assert.True(login.IsOk, $"login: {login.Status}");
        return new MobilePatient(client, store, events, patient, email);
    }

    // ---- Sesión ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Login_real_devuelve_el_nombre_y_guarda_la_sesion()
    {
        var (userId, email) = await stack.CreateUserAsync(UserRole.Paciente);
        await stack.CreatePatientAsync(userId);
        var client = NewClient(out var store, out _);

        var result = await client.LoginAsync(email, StackFixture.Password);

        Assert.True(result.IsOk);
        Assert.Equal("Usuario Paciente", result.Value);
        Assert.NotNull(await store.LoadAsync());
        Assert.Equal("Usuario Paciente", await client.GetUserNameAsync());
    }

    [Fact]
    public async Task Login_real_con_contrasena_incorrecta_o_usuario_inexistente_da_el_mismo_resultado()
    {
        var (_, email) = await stack.CreateUserAsync(UserRole.Paciente);
        var client = NewClient(out var store, out _);

        var wrong = await client.LoginAsync(email, "Incorrecta-123!");
        var unknown = await client.LoginAsync("no.existe@example.com", "Incorrecta-123!");

        Assert.Equal(ApiStatus.InvalidCredentials, wrong.Status);
        Assert.Equal(ApiStatus.InvalidCredentials, unknown.Status);
        Assert.Null(await store.LoadAsync());
    }

    [Theory]
    [InlineData(UserRole.Medico)]
    [InlineData(UserRole.Recepcion)]
    [InlineData(UserRole.Laboratorio)]
    [InlineData(UserRole.Administrador)]
    public async Task La_app_rechaza_el_inicio_de_sesion_del_personal(UserRole role)
    {
        var (_, email) = await stack.CreateUserAsync(role);
        var client = NewClient(out var store, out _);

        var result = await client.LoginAsync(email, StackFixture.Password);

        Assert.Equal(ApiStatus.Forbidden, result.Status);
        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task Cerrar_sesion_revoca_el_acceso_en_el_servidor()
    {
        var p = await NewPatientAsync();
        var copy = new InMemorySessionStore();
        await copy.SaveAsync((await p.Store.LoadAsync())!); // otra instancia con los mismos tokens (por ejemplo, un token robado)
        var other = new MobileApiClient(stack.CreateClient(), copy, TimeProvider.System, new SessionEvents());
        Assert.True((await other.GetMyAppointmentsAsync()).IsOk);

        await p.Client.LogoutAsync();

        Assert.Null(await p.Store.LoadAsync());
        Assert.Equal(ApiStatus.SessionExpired, (await other.GetMyAppointmentsAsync()).Status); // 401 y el refresco ya no sirve
        Assert.Null(await copy.LoadAsync());
    }

    [Fact]
    public async Task El_cliente_renueva_el_token_con_la_API_real_cuando_esta_por_vencer()
    {
        var (userId, email) = await stack.CreateUserAsync(UserRole.Paciente);
        await stack.CreatePatientAsync(userId);
        var client = NewClient(out var store, out _);
        await client.LoginAsync(email, StackFixture.Password);
        var before = (await store.LoadAsync())!;
        // Para el cliente ya pasaron 14 minutos y medio: debe renovar antes de llamar.
        var later = NewClient(out var laterStore, out _, new FixedOffsetTime(TimeSpan.FromMinutes(14.6)));
        await laterStore.SaveAsync(before);

        var result = await later.GetMyAppointmentsAsync();

        Assert.True(result.IsOk);
        var after = (await laterStore.LoadAsync())!;
        Assert.NotEqual(before.RefreshToken, after.RefreshToken); // el refresco rotó
        // Reusar el refresco ya rotado revoca la sesión (protección contra robo de tokens): el token viejo ya no sirve.
        var stolen = NewClient(out var stolenStore, out _, new FixedOffsetTime(TimeSpan.FromMinutes(14.6)));
        await stolenStore.SaveAsync(before);
        Assert.Equal(ApiStatus.SessionExpired, (await stolen.GetMyAppointmentsAsync()).Status);
    }

    [Fact]
    public async Task Sin_servicio_el_cliente_informa_falta_de_conexion()
    {
        var client = new MobileApiClient(new HttpClient { BaseAddress = new Uri("http://127.0.0.1:1/"), Timeout = TimeSpan.FromSeconds(3) },
            new InMemorySessionStore(), TimeProvider.System, new SessionEvents());

        Assert.Equal(ApiStatus.Offline, (await client.LoginAsync("ana@example.com", "x")).Status);
    }

    // ---- Citas ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reserva_de_punta_a_punta_con_la_API_real_consulta_detalle_y_cancelacion()
    {
        var p = await NewPatientAsync();
        var doctor = await stack.CreateScheduledDoctorAsync();
        var date = StackFixture.Future(4);

        var doctors = await p.Client.GetDoctorsAsync();
        var listed = Assert.Single(doctors.Value!, d => d.Id == doctor.Id);
        Assert.Null(listed.Phone); // el paciente no ve el contacto del médico
        var availability = (await p.Client.GetAvailabilityAsync(doctor.Id, date)).Value!;
        Assert.Contains(availability.Slots, s => s.Time == "08:00" && s.Available);

        var created = await p.Client.CreateAppointmentAsync(new CreateAppointmentRequest(null, doctor.Id, date, "08:00"), NewKey());
        Assert.True(created.IsOk);
        var mine = (await p.Client.GetMyAppointmentsAsync()).Value!;
        Assert.Equal(created.Value!.Id, Assert.Single(mine).Id);
        Assert.Equal("Confirmed", (await p.Client.GetAppointmentAsync(created.Value.Id)).Value!.Status);
        Assert.Contains(((await p.Client.GetAvailabilityAsync(doctor.Id, date)).Value!).Slots, s => s.Time == "08:00" && !s.Available);

        Assert.True((await p.Client.CancelAppointmentAsync(created.Value.Id)).IsOk);
        Assert.True((await p.Client.CancelAppointmentAsync(created.Value.Id)).IsOk); // idempotente
        Assert.Equal("Cancelled", (await p.Client.GetAppointmentAsync(created.Value.Id)).Value!.Status);
        Assert.Contains(((await p.Client.GetAvailabilityAsync(doctor.Id, date)).Value!).Slots, s => s.Time == "08:00" && s.Available); // el horario vuelve a estar libre
    }

    [Fact]
    public async Task Reintentar_la_reserva_con_la_misma_clave_no_duplica_la_cita()
    {
        var p = await NewPatientAsync();
        var doctor = await stack.CreateScheduledDoctorAsync();
        var request = new CreateAppointmentRequest(null, doctor.Id, StackFixture.Future(5), "09:00");
        var key = NewKey();

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => p.Client.CreateAppointmentAsync(request, key)));

        Assert.All(results, r => Assert.True(r.IsOk));
        Assert.Single(results.Select(r => r.Value!.Id).Distinct());
        Assert.Single((await p.Client.GetMyAppointmentsAsync()).Value!);
    }

    [Fact]
    public async Task Horario_ocupado_por_otra_persona_devuelve_Conflict_con_el_mensaje_de_la_API()
    {
        var first = await NewPatientAsync("Primera Paciente");
        var second = await NewPatientAsync("Segunda Paciente");
        var doctor = await stack.CreateScheduledDoctorAsync();
        var request = new CreateAppointmentRequest(null, doctor.Id, StackFixture.Future(6), "10:00");
        Assert.True((await first.Client.CreateAppointmentAsync(request, NewKey())).IsOk);

        var result = await second.Client.CreateAppointmentAsync(request, NewKey());

        Assert.Equal(ApiStatus.Conflict, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Empty((await second.Client.GetMyAppointmentsAsync()).Value!); // no quedó nada reservado
        // Otro horario del mismo médico y fecha sí se puede reservar.
        Assert.True((await second.Client.CreateAppointmentAsync(request with { Time = "10:30" }, NewKey())).IsOk);
    }

    [Fact]
    public async Task Fecha_fuera_de_rango_se_rechaza_con_un_mensaje_claro()
    {
        var p = await NewPatientAsync();
        var doctor = await stack.CreateScheduledDoctorAsync();

        var result = await p.Client.GetAvailabilityAsync(doctor.Id, new DateOnly(2020, 1, 1));

        Assert.Equal(ApiStatus.Invalid, result.Status);
        Assert.StartsWith("La fecha debe estar entre hoy y el", result.Message);
    }

    [Fact]
    public async Task Un_paciente_no_ve_ni_cancela_las_citas_de_otro()
    {
        var owner = await NewPatientAsync("Dueña de la cita");
        var intruder = await NewPatientAsync("Intruso");
        var doctor = await stack.CreateScheduledDoctorAsync();
        var appointment = (await owner.Client.CreateAppointmentAsync(new CreateAppointmentRequest(null, doctor.Id, StackFixture.Future(7), "08:00"), NewKey())).Value!;

        Assert.Equal(ApiStatus.NotFound, (await intruder.Client.GetAppointmentAsync(appointment.Id)).Status);
        Assert.Equal(ApiStatus.NotFound, (await intruder.Client.CancelAppointmentAsync(appointment.Id)).Status);
        Assert.Empty((await intruder.Client.GetMyAppointmentsAsync()).Value!);
        Assert.Equal("Confirmed", (await owner.Client.GetAppointmentAsync(appointment.Id)).Value!.Status);
    }

    // ---- Resultados, recetas y notificaciones ----------------------------------------------------------------------

    [Fact]
    public async Task Resultados_propios_se_listan_y_se_abren_y_los_ajenos_no()
    {
        var own = await NewPatientAsync("Paciente con resultado");
        var other = await NewPatientAsync("Otro paciente");
        var (lab, _) = await stack.LoginClientAsync(UserRole.Laboratorio);
        var created = await (await lab.PostAsJsonAsync("/api/labs/results", new CreateLabResultRequest(own.Patient.Id, "Hematología completa",
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), "Obs", [new LabResultParameterRequest("Hemoglobina", "13.2", "g/dL", "12.0–16.0")]))).Content.ReadFromJsonAsync<LabResultDto>();

        var list = (await own.Client.GetMyLabResultsAsync()).Value!;
        var detail = (await own.Client.GetLabResultAsync(created!.Id)).Value!;

        Assert.Equal(created.Id, Assert.Single(list).Id);
        Assert.Equal("Hemoglobina", Assert.Single(detail.Parameters).Name);
        Assert.Equal(ApiStatus.NotFound, (await other.Client.GetLabResultAsync(created.Id)).Status);
        Assert.Empty((await other.Client.GetMyLabResultsAsync()).Value!);
    }

    [Fact]
    public async Task Recetas_propias_se_listan_y_se_abren_y_las_ajenas_no()
    {
        var own = await NewPatientAsync("Paciente con receta");
        var other = await NewPatientAsync("Otro paciente");
        var (docUserId, email) = await stack.CreateUserAsync(UserRole.Medico);
        var doctor = await stack.CreateScheduledDoctorAsync(docUserId);
        var appointment = await stack.CreateAppointmentAsync(own.Patient.Id, doctor.Id, DateTime.UtcNow.Date.AddDays(-3).AddHours(9));
        var (doctorClient, _) = await stack.LoginClientForAsync(email);
        var consultation = (await (await doctorClient.PostAsJsonAsync("/api/consultations", new CreateConsultationRequest(appointment.Id, "Motivo", "Notas", "Dx"))).Content.ReadFromJsonAsync<ConsultationDto>())!;
        var prescription = (await (await doctorClient.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultation.Id,
            [new PrescriptionItemRequest("Paracetamol", "500 mg", "Cada 8 horas", "3 días", "Con alimentos")]))).Content.ReadFromJsonAsync<PrescriptionDto>())!;

        var list = (await own.Client.GetMyPrescriptionsAsync()).Value!;
        var detail = (await own.Client.GetPrescriptionAsync(prescription.Id)).Value!;

        Assert.Equal(prescription.Id, Assert.Single(list).Id);
        Assert.Equal("Paracetamol", Assert.Single(detail.Items).Medication);
        Assert.Equal(ApiStatus.NotFound, (await other.Client.GetPrescriptionAsync(prescription.Id)).Status);
        Assert.Empty((await other.Client.GetMyPrescriptionsAsync()).Value!);
    }

    [Fact]
    public async Task Notificaciones_propias_se_listan_se_marcan_leidas_y_apuntan_al_resultado()
    {
        var own = await NewPatientAsync("Paciente notificada");
        var other = await NewPatientAsync("Otro paciente");
        var resultId = Guid.NewGuid();
        Guid notificationId;
        using (var scope = stack.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
            var notification = new Notification { PatientId = own.Patient.Id, Type = NotificationTypes.LabResultReady, SourceId = resultId, EventId = Guid.NewGuid(), Title = "Tu resultado está disponible", Body = "Ya puedes consultar tu resultado." };
            db.Notifications.Add(notification);
            await db.SaveChangesAsync();
            notificationId = notification.Id;
        }

        var list = (await own.Client.GetNotificationsAsync()).Value!;
        var item = Assert.Single(list.Items);
        Assert.Equal((notificationId, "LabResult", resultId, false), (item.Id, item.TargetType, item.TargetId, item.IsRead));
        Assert.Equal(1, (await own.Client.GetUnreadCountAsync()).Value!.Count);

        Assert.True((await own.Client.MarkNotificationReadAsync(notificationId)).IsOk);
        Assert.Equal(0, (await own.Client.GetUnreadCountAsync()).Value!.Count);
        Assert.Equal(ApiStatus.NotFound, (await other.Client.MarkNotificationReadAsync(notificationId)).Status);
        Assert.Empty((await other.Client.GetNotificationsAsync()).Value!.Items);
    }
}

/// <summary>Reloj del cliente adelantado respecto del real (para probar la renovación anticipada sin esperar 15 minutos).</summary>
public sealed class FixedOffsetTime(TimeSpan offset) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UtcNow + offset;
}
