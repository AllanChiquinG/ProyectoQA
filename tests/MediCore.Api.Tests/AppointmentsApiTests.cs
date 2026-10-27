using System.Net;
using System.Net.Http.Json;
using MediCore.Api.Tests.Infrastructure;
using MediCore.Contracts;
using MediCore.Contracts.Audit;
using MediCore.Contracts.Patients;
using MediCore.Contracts.Scheduling;
using MediCore.Domain.Identity;
using MediCore.Domain.Scheduling;
using MediCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MediCore.Api.Tests.Infrastructure.SchedulingHelpers;

namespace MediCore.Api.Tests;

/// <summary>Reserva, consulta y cancelación de citas contra PostgreSQL y Redis reales.</summary>
[Trait("Category", "Integration")]
[Collection(StackCollection.Name)]
public class AppointmentsApiTests(StackFixture stack)
{
    private async Task<int> CountActiveAsync(Guid doctorId)
    {
        using var scope = stack.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        return await db.Appointments.CountAsync(a => a.DoctorId == doctorId && a.Status == AppointmentStatus.Confirmed);
    }

    private async Task<int> CountAllAsync(Guid doctorId)
    {
        using var scope = stack.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        return await db.Appointments.CountAsync(a => a.DoctorId == doctorId);
    }

    private async Task<List<AuditEntryDto>> AuditAsync()
    {
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);
        return (await admin.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit?take=500"))!;
    }

    // ---- Reserva y consulta -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Recepcion_reserva_y_la_cita_aparece_en_agenda_detalle_y_disponibilidad()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var (client, login) = await stack.LoginClientAsync(UserRole.Recepcion);

        var response = await client.BookAsync(Request(patient.Id, doctor.Id, date, "09:30", "Consulta general"));
        var appointment = await response.ReadAppointmentAsync();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/appointments/{appointment.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal(("09:30", date, 30, "Confirmed"), (appointment.Time, appointment.Date, appointment.DurationMinutes, appointment.Status));
        Assert.Equal(patient.FullName, appointment.PatientName);

        var detail = await client.GetFromJsonAsync<AppointmentDto>($"/api/appointments/{appointment.Id}");
        Assert.Equal(appointment.Id, detail!.Id);

        var agenda = await client.GetFromJsonAsync<List<AppointmentDto>>($"/api/appointments?doctorId={doctor.Id}&date={date:yyyy-MM-dd}");
        Assert.Equal(appointment.Id, Assert.Single(agenda!).Id);

        var availability = await client.GetFromJsonAsync<AvailabilityDto>($"/api/doctors/{doctor.Id}/availability?date={date:yyyy-MM-dd}");
        Assert.False(availability!.Slots.Single(s => s.Time == "09:30").Available);
        Assert.True(availability.Slots.Single(s => s.Time == "10:00").Available);

        var audit = await AuditAsync();
        Assert.Contains(audit, e => e.Action == "appointments.created" && e.ActorUserId == login.User.Id && e.ResourceId == appointment.Id.ToString());
        // La auditoría no contiene nombres ni el motivo de la consulta.
        Assert.DoesNotContain(audit, e => (e.Details ?? "").Contains(patient.FullName) || (e.Details ?? "").Contains("Consulta general"));
    }

    [Fact]
    public async Task Paciente_reserva_para_si_mismo_y_ve_solo_sus_citas()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var (userId, email) = await stack.CreateUserAsync(UserRole.Paciente);
        var own = await stack.CreatePatientAsync(userId);
        var other = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        await stack.CreateAppointmentAsync(other.Id, doctor.Id, date.ToDateTime(new TimeOnly(8, 0)));
        var (client, _) = await stack.LoginClientForAsync(email);

        var response = await client.BookAsync(Request(null, doctor.Id, date, "10:00"));
        var created = await response.ReadAppointmentAsync();
        var mine = await client.GetFromJsonAsync<List<AppointmentDto>>("/api/appointments/me");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(own.Id, created.PatientId);
        Assert.Equal(created.Id, Assert.Single(mine!).Id);
    }

    // ---- Concurrencia y duplicados ----------------------------------------------------------------------------------

    [Fact]
    public async Task Solicitudes_simultaneas_al_mismo_horario_producen_una_unica_reserva_activa()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var date = StackFixture.Future();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var patients = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => stack.CreatePatientAsync()));

        var responses = await Task.WhenAll(patients.Select(p => client.BookAsync(Request(p.Id, doctor.Id, date, "09:00"))));

        var summary = string.Join(", ", responses.GroupBy(r => r.StatusCode).Select(g => $"{g.Key}={g.Count()}"));
        Assert.True(1 == responses.Count(r => r.StatusCode == HttpStatusCode.Created) && 11 == responses.Count(r => r.StatusCode == HttpStatusCode.Conflict), summary);
        Assert.Equal(1, await CountActiveAsync(doctor.Id));
        var loser = responses.First(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("ocupado", await loser.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Alta_demanda_en_varios_horarios_nunca_supera_una_cita_por_horario()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var date = StackFixture.Future();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var patients = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => stack.CreatePatientAsync()));
        var times = new[] { "08:00", "08:30", "09:00", "09:30" };

        // 24 pacientes compiten por 4 horarios mientras otros consultan la disponibilidad.
        var bookings = patients.Select((p, i) => client.BookAsync(Request(p.Id, doctor.Id, date, times[i % times.Length]))).ToList();
        var reads = Enumerable.Range(0, 20).Select(_ => client.GetAsync($"/api/doctors/{doctor.Id}/availability?date={date:yyyy-MM-dd}")).ToList();
        await Task.WhenAll(bookings.Concat(reads));

        Assert.Equal(4, bookings.Count(t => t.Result.StatusCode == HttpStatusCode.Created));
        Assert.Equal(20, bookings.Count(t => t.Result.StatusCode == HttpStatusCode.Conflict));
        Assert.All(reads, t => Assert.Equal(HttpStatusCode.OK, t.Result.StatusCode));
        Assert.Equal(4, await CountActiveAsync(doctor.Id));
    }

    [Fact]
    public async Task Un_paciente_no_puede_tener_dos_citas_al_mismo_tiempo_con_distintos_medicos()
    {
        var date = StackFixture.Future();
        var patient = await stack.CreatePatientAsync();
        var doctors = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => stack.CreateScheduledDoctorAsync()));
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);

        var responses = await Task.WhenAll(doctors.Select(d => client.BookAsync(Request(patient.Id, d.Id, date, "11:00"))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        var conflict = responses.First(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("ya tiene una cita", await conflict.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Reenviar_la_misma_solicitud_con_la_misma_clave_devuelve_la_cita_original_sin_duplicar()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var key = NewKey();
        var request = Request(patient.Id, doctor.Id, date, "09:00");

        var first = await client.BookAsync(request, key);
        var second = await client.BookAsync(request, key);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal((await first.ReadAppointmentAsync()).Id, (await second.ReadAppointmentAsync()).Id);
        Assert.Equal(1, await CountAllAsync(doctor.Id));
    }

    [Fact]
    public async Task Reenvios_simultaneos_con_la_misma_clave_crean_una_sola_cita()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var key = NewKey();
        var request = Request(patient.Id, doctor.Id, date, "10:30");

        var responses = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => client.BookAsync(request, key)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK, $"estado inesperado: {r.StatusCode}"));
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var ids = (await Task.WhenAll(responses.Select(r => r.ReadAppointmentAsync()))).Select(a => a.Id).Distinct();
        Assert.Single(ids);
        Assert.Equal(1, await CountAllAsync(doctor.Id));
    }

    [Fact]
    public async Task Reenviar_sin_clave_la_misma_reserva_no_crea_otra_cita()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var request = Request(patient.Id, doctor.Id, date, "09:00");

        var first = await client.BookAsync(request);
        var second = await client.BookAsync(request);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal(1, await CountAllAsync(doctor.Id));
    }

    [Fact]
    public async Task Reutilizar_una_clave_con_otra_solicitud_devuelve_409()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var key = NewKey();

        await client.BookAsync(Request(patient.Id, doctor.Id, date, "09:00"), key);
        var different = await client.BookAsync(Request(patient.Id, doctor.Id, date, "10:00"), key);

        Assert.Equal(HttpStatusCode.Conflict, different.StatusCode);
        Assert.Equal(1, await CountAllAsync(doctor.Id));
    }

    [Theory]
    [InlineData("corta")]
    [InlineData("tiene espacios y símbolos!")]
    public async Task Clave_de_idempotencia_con_formato_invalido_devuelve_400(string key)
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);

        var response = await client.BookAsync(Request(patient.Id, doctor.Id, StackFixture.Future(), "09:00"), key);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Cancelación ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cancelar_libera_el_horario_para_otro_paciente_y_es_idempotente()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var first = await stack.CreatePatientAsync();
        var second = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var (client, login) = await stack.LoginClientAsync(UserRole.Recepcion);
        var booked = await (await client.BookAsync(Request(first.Id, doctor.Id, date, "09:00"))).ReadAppointmentAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await client.BookAsync(Request(second.Id, doctor.Id, date, "09:00"))).StatusCode);

        var cancel = await client.PostAsync($"/api/appointments/{booked.Id}/cancel", null);
        var cancelAgain = await client.PostAsync($"/api/appointments/{booked.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, cancelAgain.StatusCode);
        var availability = await client.GetFromJsonAsync<AvailabilityDto>($"/api/doctors/{doctor.Id}/availability?date={date:yyyy-MM-dd}");
        Assert.True(availability!.Slots.Single(s => s.Time == "09:00").Available);
        Assert.Empty((await client.GetFromJsonAsync<List<AppointmentDto>>($"/api/appointments?doctorId={doctor.Id}&date={date:yyyy-MM-dd}"))!);
        Assert.Equal("Cancelled", (await client.GetFromJsonAsync<AppointmentDto>($"/api/appointments/{booked.Id}"))!.Status);

        var rebooked = await client.BookAsync(Request(second.Id, doctor.Id, date, "09:00"));
        Assert.Equal(HttpStatusCode.Created, rebooked.StatusCode);

        var audit = await AuditAsync();
        Assert.Equal(1, audit.Count(e => e.Action == "appointments.cancelled" && e.ResourceId == booked.Id.ToString() && e.ActorUserId == login.User.Id));
    }

    [Fact]
    public async Task Cancelaciones_simultaneas_de_la_misma_cita_son_seguras()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var booked = await (await client.BookAsync(Request(patient.Id, doctor.Id, StackFixture.Future(), "09:00"))).ReadAppointmentAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsync($"/api/appointments/{booked.Id}/cancel", null)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Equal(0, await CountActiveAsync(doctor.Id));
        Assert.Equal(1, (await AuditAsync()).Count(e => e.Action == "appointments.cancelled" && e.ResourceId == booked.Id.ToString()));
    }

    [Fact]
    public async Task No_se_puede_cancelar_una_cita_que_ya_paso()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var past = await stack.CreateAppointmentAsync(patient.Id, doctor.Id, DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-3).AddHours(9), DateTimeKind.Unspecified));
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);

        var response = await client.PostAsync($"/api/appointments/{past.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Paciente_cancela_su_propia_cita()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var (userId, email) = await stack.CreateUserAsync(UserRole.Paciente);
        await stack.CreatePatientAsync(userId);
        var (client, _) = await stack.LoginClientForAsync(email);
        var booked = await (await client.BookAsync(Request(null, doctor.Id, StackFixture.Future(), "09:00"))).ReadAppointmentAsync();

        var cancel = await client.PostAsync($"/api/appointments/{booked.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.NoContent, cancel.StatusCode);
        Assert.Equal(0, await CountActiveAsync(doctor.Id));
    }

    // ---- Acceso a citas y pacientes ajenos --------------------------------------------------------------------------

    [Fact]
    public async Task Paciente_no_accede_ni_reserva_ni_cancela_citas_de_otro_y_queda_auditado()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var victim = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var theirs = await stack.CreateAppointmentAsync(victim.Id, doctor.Id, date.ToDateTime(new TimeOnly(9, 0)));
        var (userId, email) = await stack.CreateUserAsync(UserRole.Paciente);
        await stack.CreatePatientAsync(userId);
        var (client, login) = await stack.LoginClientForAsync(email);

        var read = await client.GetAsync($"/api/appointments/{theirs.Id}");
        var cancel = await client.PostAsync($"/api/appointments/{theirs.Id}/cancel", null);
        var bookForOther = await client.BookAsync(Request(victim.Id, doctor.Id, date, "10:00"));

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, cancel.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, bookForOther.StatusCode);
        Assert.Equal(1, await CountActiveAsync(doctor.Id));
        var audit = await AuditAsync();
        Assert.True(audit.Count(e => e.Action == "appointments.access_denied" && e.Outcome == "Failure" && e.ActorUserId == login.User.Id) >= 3);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/appointments?doctorId={doctor.Id}&date={date:yyyy-MM-dd}")).StatusCode);
    }

    [Fact]
    public async Task Medico_ve_la_agenda_y_las_citas_de_sus_pacientes_pero_no_las_de_otro_medico()
    {
        var (docUserId, docEmail) = await stack.CreateUserAsync(UserRole.Medico);
        var ownDoctor = await stack.CreateScheduledDoctorAsync(docUserId);
        var otherDoctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var ownAppointment = await stack.CreateAppointmentAsync(patient.Id, ownDoctor.Id, date.ToDateTime(new TimeOnly(9, 0)));
        var otherAppointment = await stack.CreateAppointmentAsync((await stack.CreatePatientAsync()).Id, otherDoctor.Id, date.ToDateTime(new TimeOnly(9, 0)));
        var (client, _) = await stack.LoginClientForAsync(docEmail);

        var ownAgenda = await client.GetFromJsonAsync<List<AppointmentDto>>($"/api/appointments?date={date:yyyy-MM-dd}");
        var read = await client.GetAsync($"/api/appointments/{ownAppointment.Id}");
        var otherAgenda = await client.GetAsync($"/api/appointments?doctorId={otherDoctor.Id}&date={date:yyyy-MM-dd}");
        var otherRead = await client.GetAsync($"/api/appointments/{otherAppointment.Id}");

        Assert.Equal(ownAppointment.Id, Assert.Single(ownAgenda!).Id);
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherAgenda.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, otherRead.StatusCode);
        // El médico no reserva ni cancela.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.BookAsync(Request(patient.Id, ownDoctor.Id, date, "10:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/appointments/{ownAppointment.Id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task Medico_accede_a_pacientes_solo_si_tiene_una_cita_activa_con_ellos()
    {
        var (docUserId, docEmail) = await stack.CreateUserAsync(UserRole.Medico);
        var doctor = await stack.CreateScheduledDoctorAsync(docUserId);
        var attended = await stack.CreatePatientAsync(name: "Atendido Relacion");
        var stranger = await stack.CreatePatientAsync(name: "Ajeno Relacion");
        var cancelled = await stack.CreatePatientAsync(name: "Cancelado Relacion");
        var date = StackFixture.Future();
        await stack.CreateAppointmentAsync(attended.Id, doctor.Id, date.ToDateTime(new TimeOnly(9, 0)));
        await stack.CreateAppointmentAsync(cancelled.Id, doctor.Id, date.ToDateTime(new TimeOnly(10, 0)), AppointmentStatus.Cancelled);
        var (client, _) = await stack.LoginClientForAsync(docEmail);

        var list = await client.GetFromJsonAsync<PagedResult<PatientDto>>("/api/patients?q=Relacion");

        Assert.Equal(attended.Id, Assert.Single(list!.Items).Id);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/patients/{attended.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{stranger.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{cancelled.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/patients/{attended.Id}",
            new PatientRequest("X Y", "ZZZ-1", new DateOnly(1990, 1, 1), "5550 0000", "x@example.com"))).StatusCode);
    }

    [Fact]
    public async Task Cancelar_la_cita_quita_al_medico_el_acceso_al_paciente()
    {
        var (docUserId, docEmail) = await stack.CreateUserAsync(UserRole.Medico);
        var doctor = await stack.CreateScheduledDoctorAsync(docUserId);
        var patient = await stack.CreatePatientAsync();
        var appointment = await stack.CreateAppointmentAsync(patient.Id, doctor.Id, StackFixture.Future().ToDateTime(new TimeOnly(9, 0)));
        var (doctorClient, _) = await stack.LoginClientForAsync(docEmail);
        Assert.Equal(HttpStatusCode.OK, (await doctorClient.GetAsync($"/api/patients/{patient.Id}")).StatusCode);

        var (reception, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        await reception.PostAsync($"/api/appointments/{appointment.Id}/cancel", null);

        Assert.Equal(HttpStatusCode.NotFound, (await doctorClient.GetAsync($"/api/patients/{patient.Id}")).StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Administrador)]
    [InlineData(UserRole.Laboratorio)]
    public async Task Administrador_y_laboratorio_no_acceden_a_citas(UserRole role)
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var appointment = await stack.CreateAppointmentAsync(patient.Id, doctor.Id, date.ToDateTime(new TimeOnly(9, 0)));
        var (client, _) = await stack.LoginClientAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/appointments/{appointment.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/appointments?doctorId={doctor.Id}&date={date:yyyy-MM-dd}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.BookAsync(Request(patient.Id, doctor.Id, date, "10:00"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/appointments/{appointment.Id}/cancel", null)).StatusCode);
    }

    [Fact]
    public async Task Sin_sesion_no_se_accede_a_las_citas()
    {
        var client = stack.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/appointments/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.BookAsync(Request(Guid.NewGuid(), Guid.NewGuid(), StackFixture.Future()))).StatusCode);
    }

    // ---- Datos inválidos --------------------------------------------------------------------------------------------

    public static TheoryData<string, int, string> InvalidBookings => new()
    {
        { "No_en_la_agenda_del_medico_13_00", 3, "13:00" },
        { "No_alineado_a_30_min_08_15", 3, "08:15" },
        { "Formato_de_hora_incorrecto_8_30", 3, "8:30" },
        { "Hora_inexistente_25_00", 3, "25:00" },
        { "Fecha_pasada", -2, "09:00" },
        { "Fecha_mas_alla_del_limite", 400, "09:00" },
    };

    [Theory]
    [MemberData(nameof(InvalidBookings))]
    public async Task Horario_o_fecha_invalidos_devuelven_400_y_no_crean_citas(string caso, int daysFromToday, string time)
    {
        _ = caso;
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(daysFromToday);

        var response = await client.BookAsync(Request(patient.Id, doctor.Id, date, time));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await CountAllAsync(doctor.Id));
    }

    [Fact]
    public async Task Horario_de_hoy_que_ya_paso_devuelve_400()
    {
        var doctor = await stack.CreateScheduledDoctorAsync(start: "00:00", end: "23:30");
        var patient = await stack.CreatePatientAsync();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);
        var clinicToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "America/Guatemala"));

        var response = await client.BookAsync(Request(patient.Id, doctor.Id, clinicToday, "00:00"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("ya pasó", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Dia_sin_atencion_devuelve_400()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);
        var date = StackFixture.Future(10);
        var otherDays = Enum.GetValues<DayOfWeek>().Where(d => d != date.DayOfWeek).Select(d => (int)d).ToArray();
        await admin.PutAsJsonAsync($"/api/doctors/{doctor.Id}/schedule", new ScheduleRequest(otherDays, "08:00", "12:00"));
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);

        var response = await client.BookAsync(Request(patient.Id, doctor.Id, date, "09:00"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Datos_faltantes_o_inexistentes_devuelven_el_error_correcto()
    {
        var doctor = await stack.CreateScheduledDoctorAsync();
        var patient = await stack.CreatePatientAsync();
        var date = StackFixture.Future();
        var (client, _) = await stack.LoginClientAsync(UserRole.Recepcion);

        Assert.Equal(HttpStatusCode.BadRequest, (await client.BookAsync(Request(null, doctor.Id, date))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.BookAsync(Request(Guid.NewGuid(), doctor.Id, date))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.BookAsync(Request(patient.Id, Guid.NewGuid(), date))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/appointments", new { patientId = patient.Id, time = "09:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.BookAsync(Request(patient.Id, doctor.Id, date, "09:00", new string('x', 121)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/appointments/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/appointments/{Guid.NewGuid()}/cancel", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/appointments?doctorId={doctor.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/appointments?date=2030-01-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/api/appointments?doctorId={doctor.Id}&date=no-es-fecha")).StatusCode);
        Assert.Equal(0, await CountAllAsync(doctor.Id));
    }
}
