using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediCore.Api.Tests.Infrastructure;
using MediCore.Contracts;
using MediCore.Contracts.Audit;
using MediCore.Contracts.Clinical;
using MediCore.Contracts.Patients;
using MediCore.Domain.Identity;
using MediCore.Domain.Scheduling;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static MediCore.Api.Tests.Infrastructure.SchedulingHelpers;

namespace MediCore.Api.Tests;

/// <summary>Expediente clínico: consultas y recetas, autorización por rol y por recurso, y auditoría sin contenido clínico.</summary>
[Trait("Category", "Integration")]
[Collection(StackCollection.Name)]
public class ClinicalApiTests(StackFixture stack)
{
    private sealed record Scenario(HttpClient Client, string Email, Guid DoctorId, Domain.Patients.Patient Patient, Appointment Appointment);

    /// <summary>Un médico con cuenta, un paciente y una cita de ese médico ya pasada (se puede registrar su consulta).</summary>
    private async Task<Scenario> NewScenarioAsync(Guid? patientUserId = null)
    {
        var (docUserId, email) = await stack.CreateUserAsync(UserRole.Medico);
        var doctor = await stack.CreateScheduledDoctorAsync(docUserId);
        var patient = await stack.CreatePatientAsync(patientUserId);
        var appointment = await stack.CreateAppointmentAsync(patient.Id, doctor.Id, DateTime.UtcNow.Date.AddDays(-3).AddHours(9));
        var (client, _) = await stack.LoginClientForAsync(email);
        return new Scenario(client, email, doctor.Id, patient, appointment);
    }

    private static CreateConsultationRequest Consultation(Guid appointmentId, string token = "motivo")
        => new(appointmentId, $"Motivo {token}", $"Notas {token}", $"Diagnóstico {token}");

    private static PrescriptionItemRequest Item(string medication = "Paracetamol", string token = "x")
        => new(medication, "500 mg", "Cada 8 horas", "3 días", $"Tomar con alimentos {token}");

    private async Task<ConsultationDto> RegisterConsultationAsync(Scenario s, string token = "motivo")
    {
        var response = await s.Client.PostAsJsonAsync("/api/consultations", Consultation(s.Appointment.Id, token));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ConsultationDto>())!;
    }

    private async Task<List<AuditEntryDto>> AuditAsync()
    {
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);
        return (await admin.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit?take=500"))!;
    }

    // ---- Consultas --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Medico_registra_la_consulta_de_su_cita_y_la_ve_en_el_expediente()
    {
        var s = await NewScenarioAsync();

        var response = await s.Client.PostAsJsonAsync("/api/consultations", Consultation(s.Appointment.Id));
        var created = (await response.Content.ReadFromJsonAsync<ConsultationDto>())!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/consultations/{created.Id}", response.Headers.Location!.AbsolutePath);
        Assert.Equal((s.Patient.Id, s.Appointment.Id, DateOnly.FromDateTime(s.Appointment.StartsAtLocal)), (created.PatientId, created.AppointmentId, created.Date));
        Assert.Equal("Diagnóstico motivo", created.Diagnosis);

        var list = await s.Client.GetFromJsonAsync<List<ConsultationDto>>($"/api/patients/{s.Patient.Id}/consultations");
        Assert.Equal(created.Id, Assert.Single(list!).Id);
        Assert.Equal(created.Id, (await s.Client.GetFromJsonAsync<ConsultationDto>($"/api/consultations/{created.Id}"))!.Id);
    }

    [Fact]
    public async Task Citas_consultables_son_las_confirmadas_de_hoy_o_antes_y_sin_consulta()
    {
        var (docUserId, email) = await stack.CreateUserAsync(UserRole.Medico);
        var doctor = await stack.CreateScheduledDoctorAsync(docUserId);
        var patient = await stack.CreatePatientAsync();
        var past = await stack.CreateAppointmentAsync(patient.Id, doctor.Id, DateTime.UtcNow.Date.AddDays(-5).AddHours(9));
        var done = await stack.CreateAppointmentAsync(patient.Id, doctor.Id, DateTime.UtcNow.Date.AddDays(-4).AddHours(9));
        await stack.CreateConsultationAsync(done);
        await stack.CreateAppointmentAsync(patient.Id, doctor.Id, DateTime.UtcNow.Date.AddDays(-3).AddHours(9), AppointmentStatus.Cancelled);
        await stack.CreateAppointmentAsync(patient.Id, doctor.Id, DateTime.UtcNow.Date.AddDays(5).AddHours(9));
        var (client, _) = await stack.LoginClientForAsync(email);

        var options = await client.GetFromJsonAsync<List<ConsultableAppointmentDto>>($"/api/appointments/consultable?patientId={patient.Id}");

        Assert.Equal(past.Id, Assert.Single(options!).Id);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/appointments/consultable")).StatusCode);
    }

    [Fact]
    public async Task La_consulta_exige_una_cita_propia_confirmada_y_de_hoy_o_anterior_y_no_se_repite()
    {
        var s = await NewScenarioAsync();
        var otherDoctor = await stack.CreateScheduledDoctorAsync();
        var foreign = await stack.CreateAppointmentAsync(s.Patient.Id, otherDoctor.Id, DateTime.UtcNow.Date.AddDays(-2).AddHours(9));
        var cancelled = await stack.CreateAppointmentAsync(s.Patient.Id, s.DoctorId, DateTime.UtcNow.Date.AddDays(-6).AddHours(9), AppointmentStatus.Cancelled);
        var tomorrow = await stack.CreateAppointmentAsync(s.Patient.Id, s.DoctorId, StackFixture.ClinicNow.Date.AddDays(1).AddHours(9));
        var laterToday = await stack.CreateAppointmentAsync(s.Patient.Id, s.DoctorId, StackFixture.ClinicNow.Date.AddHours(23));

        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsJsonAsync("/api/consultations", Consultation(foreign.Id))).StatusCode);   // de otro médico
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsJsonAsync("/api/consultations", Consultation(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsJsonAsync("/api/consultations", Consultation(cancelled.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsJsonAsync("/api/consultations", Consultation(tomorrow.Id))).StatusCode);   // futura
        Assert.Equal(HttpStatusCode.Created, (await s.Client.PostAsJsonAsync("/api/consultations", Consultation(laterToday.Id))).StatusCode);  // hoy: se admite
        Assert.Equal(HttpStatusCode.Created, (await s.Client.PostAsJsonAsync("/api/consultations", Consultation(s.Appointment.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await s.Client.PostAsJsonAsync("/api/consultations", Consultation(s.Appointment.Id))).StatusCode); // repetida

        var audit = await AuditAsync();
        Assert.Contains(audit, e => e.Action == "clinical.access_denied" && e.Outcome == "Failure" && e.ResourceId == foreign.Id.ToString());
    }

    [Fact]
    public async Task Envios_simultaneos_de_la_misma_consulta_crean_una_sola()
    {
        var s = await NewScenarioAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => s.Client.PostAsJsonAsync("/api/consultations", Consultation(s.Appointment.Id))));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
        Assert.Single((await s.Client.GetFromJsonAsync<List<ConsultationDto>>($"/api/patients/{s.Patient.Id}/consultations"))!);
    }

    [Fact]
    public async Task Consulta_con_datos_faltantes_o_demasiado_largos_devuelve_400()
    {
        var s = await NewScenarioAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/consultations", new CreateConsultationRequest(null, "a", "b", "c"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/consultations", new CreateConsultationRequest(s.Appointment.Id, "", "b", "c"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/consultations", new CreateConsultationRequest(s.Appointment.Id, "a", new string('x', 4001), "c"))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/consultations", new CreateConsultationRequest(s.Appointment.Id, "a", "b", new string('x', 501)))).StatusCode);
        Assert.Empty((await s.Client.GetFromJsonAsync<List<ConsultationDto>>($"/api/patients/{s.Patient.Id}/consultations"))!);
    }

    // ---- Relación médico–paciente mediante consultas ---------------------------------------------------------------

    [Fact]
    public async Task Una_consulta_registrada_crea_y_conserva_la_relacion_de_atencion()
    {
        var s = await NewScenarioAsync();
        var withoutConsultation = await stack.CreatePatientAsync();
        var cancelledOnly = await stack.CreateAppointmentAsync(withoutConsultation.Id, s.DoctorId, DateTime.UtcNow.Date.AddDays(-8).AddHours(9), AppointmentStatus.Cancelled);

        await RegisterConsultationAsync(s);
        await stack.SetAppointmentStatusAsync(s.Appointment.Id, AppointmentStatus.Cancelled); // la cita queda cancelada; la consulta ya existe

        // Con la consulta, el acceso se conserva aunque la cita se cancele.
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync($"/api/patients/{s.Patient.Id}")).StatusCode);
        var list = await s.Client.GetFromJsonAsync<PagedResult<PatientDto>>($"/api/patients?q={Uri.EscapeDataString(s.Patient.FullName)}&pageSize=100");
        Assert.Contains(list!.Items, p => p.Id == s.Patient.Id);
        // Sin consulta y solo con una cita cancelada, no hay relación.
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/patients/{withoutConsultation.Id}")).StatusCode);
        _ = cancelledOnly;
    }

    [Fact]
    public async Task Cancelar_la_cita_con_otra_cita_valida_o_con_consulta_mantiene_el_acceso_y_sin_nada_lo_retira()
    {
        var s = await NewScenarioAsync();
        var second = await stack.CreateAppointmentAsync(s.Patient.Id, s.DoctorId, DateTime.UtcNow.Date.AddDays(-2).AddHours(9));

        await stack.SetAppointmentStatusAsync(s.Appointment.Id, AppointmentStatus.Cancelled);
        Assert.Equal(HttpStatusCode.OK, (await s.Client.GetAsync($"/api/patients/{s.Patient.Id}")).StatusCode); // queda la segunda cita

        await stack.SetAppointmentStatusAsync(second.Id, AppointmentStatus.Cancelled);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.GetAsync($"/api/patients/{s.Patient.Id}")).StatusCode); // ya no queda ninguna relación
    }

    // ---- Expediente: solo lo escrito por el propio médico -----------------------------------------------------------

    [Fact]
    public async Task Cada_medico_ve_en_el_expediente_solo_lo_que_el_escribio()
    {
        var a = await NewScenarioAsync();
        var (userB, emailB) = await stack.CreateUserAsync(UserRole.Medico);
        var doctorB = await stack.CreateScheduledDoctorAsync(userB);
        var appointmentB = await stack.CreateAppointmentAsync(a.Patient.Id, doctorB.Id, DateTime.UtcNow.Date.AddDays(-2).AddHours(9));
        var (clientB, _) = await stack.LoginClientForAsync(emailB);
        var consultationA = await RegisterConsultationAsync(a, "de A");
        var consultationB = (await (await clientB.PostAsJsonAsync("/api/consultations", Consultation(appointmentB.Id, "de B"))).Content.ReadFromJsonAsync<ConsultationDto>())!;
        await a.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultationA.Id, [Item("Medicamento de A")]));
        await clientB.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultationB.Id, [Item("Medicamento de B")]));

        var seenByA = await a.Client.GetFromJsonAsync<List<ConsultationDto>>($"/api/patients/{a.Patient.Id}/consultations");
        var seenByB = await clientB.GetFromJsonAsync<List<ConsultationDto>>($"/api/patients/{a.Patient.Id}/consultations");
        var rxA = await a.Client.GetFromJsonAsync<List<PrescriptionDto>>($"/api/patients/{a.Patient.Id}/prescriptions");

        Assert.Equal(consultationA.Id, Assert.Single(seenByA!).Id);
        Assert.Equal(consultationB.Id, Assert.Single(seenByB!).Id);
        Assert.Equal("Medicamento de A", Assert.Single(Assert.Single(rxA!).Items).Medication);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.GetAsync($"/api/consultations/{consultationB.Id}")).StatusCode);
    }

    [Fact]
    public async Task Medico_sin_relacion_no_accede_al_expediente_y_el_intento_queda_auditado()
    {
        var stranger = await stack.CreatePatientAsync();
        var (userId, email) = await stack.CreateUserAsync(UserRole.Medico);
        await stack.CreateScheduledDoctorAsync(userId);
        var (client, login) = await stack.LoginClientForAsync(email);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{stranger.Id}/consultations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{stranger.Id}/prescriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/patients/{Guid.NewGuid()}/consultations")).StatusCode);

        var audit = await AuditAsync();
        Assert.True(audit.Count(e => e.Action == "clinical.access_denied" && e.ActorUserId == login.User.Id && e.ResourceId == stranger.Id.ToString()) >= 2);
    }

    [Theory]
    [InlineData(UserRole.Recepcion)]
    [InlineData(UserRole.Administrador)]
    [InlineData(UserRole.Laboratorio)]
    [InlineData(UserRole.Paciente)]
    public async Task Los_demas_roles_no_acceden_a_consultas_ni_crean_recetas(UserRole role)
    {
        var s = await NewScenarioAsync();
        var consultation = await RegisterConsultationAsync(s);
        var (client, _) = await stack.LoginClientAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/patients/{s.Patient.Id}/consultations")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/patients/{s.Patient.Id}/prescriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/consultations/{consultation.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/consultations", Consultation(s.Appointment.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultation.Id, [Item()]))).StatusCode);
    }

    // ---- Recetas ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Receta_con_varios_medicamentos_conserva_dosis_frecuencia_duracion_e_indicaciones_en_orden()
    {
        var s = await NewScenarioAsync();
        var consultation = await RegisterConsultationAsync(s);
        var items = new List<PrescriptionItemRequest>
        {
            new("Paracetamol", "500 mg", "Cada 8 horas", "3 días", "Con alimentos."),
            new("Ibuprofeno", "400 mg", "Cada 12 horas", "5 días", "Después de comer."),
            new("Omeprazol", "20 mg", "Una vez al día", "14 días", "En ayunas."),
        };

        var response = await s.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultation.Id, items));
        var created = (await response.Content.ReadFromJsonAsync<PrescriptionDto>())!;
        var fetched = await s.Client.GetFromJsonAsync<PrescriptionDto>($"/api/prescriptions/{created.Id}");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["Paracetamol", "Ibuprofeno", "Omeprazol"], fetched!.Items.Select(i => i.Medication));
        Assert.Equal(("400 mg", "Cada 12 horas", "5 días", "Después de comer."), (fetched.Items[1].Dose, fetched.Items[1].Frequency, fetched.Items[1].Duration, fetched.Items[1].Instructions));
        Assert.Equal((s.Patient.Id, consultation.Id, s.Patient.FullName), (fetched.PatientId, fetched.ConsultationId, fetched.PatientName));
        Assert.Single((await s.Client.GetFromJsonAsync<List<PrescriptionDto>>($"/api/patients/{s.Patient.Id}/prescriptions"))!);
    }

    [Fact]
    public async Task Receta_invalida_devuelve_400_y_no_se_crea()
    {
        var s = await NewScenarioAsync();
        var consultation = await RegisterConsultationAsync(s);

        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultation.Id, []))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/prescriptions",
            new CreatePrescriptionRequest(consultation.Id, Enumerable.Range(0, 11).Select(i => Item($"Med {i}")).ToList()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/prescriptions",
            new CreatePrescriptionRequest(consultation.Id, [new PrescriptionItemRequest("", "1", "1", "1", "1")]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/prescriptions",
            new CreatePrescriptionRequest(consultation.Id, [new PrescriptionItemRequest("M", "D", "F", "T", new string('x', 501))]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await s.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(null, [Item()]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await s.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(Guid.NewGuid(), [Item()]))).StatusCode);
        Assert.Empty((await s.Client.GetFromJsonAsync<List<PrescriptionDto>>($"/api/patients/{s.Patient.Id}/prescriptions"))!);
    }

    [Fact]
    public async Task Receta_solo_para_consultas_del_propio_medico()
    {
        var s = await NewScenarioAsync();
        var consultation = await RegisterConsultationAsync(s);
        var (_, otherEmail) = await stack.CreateUserAsync(UserRole.Medico);
        var (other, _) = await stack.LoginClientForAsync(otherEmail);

        var response = await other.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultation.Id, [Item()]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains(await AuditAsync(), e => e.Action == "clinical.access_denied" && e.ResourceId == consultation.Id.ToString());
    }

    [Fact]
    public async Task Paciente_solo_consulta_sus_propias_recetas()
    {
        var (patientUserId, patientEmail) = await stack.CreateUserAsync(UserRole.Paciente);
        var s = await NewScenarioAsync(patientUserId);
        var consultation = await RegisterConsultationAsync(s);
        var prescription = (await (await s.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultation.Id, [Item("Amoxicilina")])))
            .Content.ReadFromJsonAsync<PrescriptionDto>())!;

        var (otherUserId, otherEmail) = await stack.CreateUserAsync(UserRole.Paciente);
        await stack.CreatePatientAsync(otherUserId);
        var (owner, ownerLogin) = await stack.LoginClientForAsync(patientEmail);
        var (stranger, strangerLogin) = await stack.LoginClientForAsync(otherEmail);

        var mine = await owner.GetFromJsonAsync<List<PrescriptionDto>>("/api/prescriptions/me");
        Assert.Equal(prescription.Id, Assert.Single(mine!).Id);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync($"/api/prescriptions/{prescription.Id}")).StatusCode);

        Assert.Empty((await stranger.GetFromJsonAsync<List<PrescriptionDto>>("/api/prescriptions/me"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/prescriptions/{prescription.Id}")).StatusCode);
        // El paciente no entra al expediente clínico del médico.
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync($"/api/patients/{s.Patient.Id}/consultations")).StatusCode);

        var audit = await AuditAsync();
        Assert.Contains(audit, e => e.Action == "clinical.access_denied" && e.ActorUserId == strangerLogin.User.Id && e.ResourceId == prescription.Id.ToString());
        Assert.Contains(audit, e => e.Action == "prescriptions.read" && e.ActorUserId == ownerLogin.User.Id);
    }

    [Fact]
    public async Task Otro_medico_con_relacion_con_el_paciente_no_lee_recetas_de_un_colega()
    {
        var a = await NewScenarioAsync();
        var consultation = await RegisterConsultationAsync(a);
        var prescription = (await (await a.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultation.Id, [Item()]))).Content.ReadFromJsonAsync<PrescriptionDto>())!;
        var (userB, emailB) = await stack.CreateUserAsync(UserRole.Medico);
        var doctorB = await stack.CreateScheduledDoctorAsync(userB);
        await stack.CreateAppointmentAsync(a.Patient.Id, doctorB.Id, DateTime.UtcNow.Date.AddDays(-1).AddHours(9));
        var (clientB, _) = await stack.LoginClientForAsync(emailB);

        Assert.Equal(HttpStatusCode.OK, (await clientB.GetAsync($"/api/patients/{a.Patient.Id}")).StatusCode); // sí es su paciente (tiene cita)
        Assert.Equal(HttpStatusCode.NotFound, (await clientB.GetAsync($"/api/prescriptions/{prescription.Id}")).StatusCode); // pero no la receta de su colega
        Assert.Empty((await clientB.GetFromJsonAsync<List<PrescriptionDto>>($"/api/patients/{a.Patient.Id}/prescriptions"))!);
    }

    [Fact]
    public async Task Recepcion_administrador_y_laboratorio_no_leen_recetas()
    {
        var s = await NewScenarioAsync();
        var consultation = await RegisterConsultationAsync(s);
        var prescription = (await (await s.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultation.Id, [Item()]))).Content.ReadFromJsonAsync<PrescriptionDto>())!;

        foreach (var role in new[] { UserRole.Recepcion, UserRole.Administrador, UserRole.Laboratorio })
        {
            var (client, _) = await stack.LoginClientAsync(role);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/prescriptions/{prescription.Id}")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await stack.CreateClient().GetAsync($"/api/prescriptions/{prescription.Id}")).StatusCode);
    }

    // ---- Auditoría y bitácoras sin contenido clínico ----------------------------------------------------------------

    [Fact]
    public async Task Ni_la_auditoria_ni_las_bitacoras_contienen_el_contenido_clinico()
    {
        var logs = new CapturingLoggerProvider();
        var factory = stack.WithWebHostBuilder(b => b
            .ConfigureLogging(l => { l.SetMinimumLevel(LogLevel.Trace); l.AddProvider(logs); }));
        var token = $"SECRETO{Guid.NewGuid():N}"[..20];
        var (docUserId, email) = await stack.CreateUserAsync(UserRole.Medico);
        var doctor = await stack.CreateScheduledDoctorAsync(docUserId);
        var patient = await stack.CreatePatientAsync();
        var appointment = await stack.CreateAppointmentAsync(patient.Id, doctor.Id, DateTime.UtcNow.Date.AddDays(-3).AddHours(9));
        var client = factory.CreateClient();
        var login = await StackFixture.LoginAsync(client, email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var consultation = (await (await client.PostAsJsonAsync("/api/consultations",
            new CreateConsultationRequest(appointment.Id, $"Motivo {token}", $"Notas {token}", $"Diagnóstico {token}"))).Content.ReadFromJsonAsync<ConsultationDto>())!;
        var prescription = (await (await client.PostAsJsonAsync("/api/prescriptions",
            new CreatePrescriptionRequest(consultation.Id, [new PrescriptionItemRequest($"Medicamento {token}", "1", "2", "3", $"Indicaciones {token}")]))).Content.ReadFromJsonAsync<PrescriptionDto>())!;
        await client.GetAsync($"/api/patients/{patient.Id}/consultations");
        await client.GetAsync($"/api/patients/{patient.Id}/prescriptions");
        await client.GetAsync($"/api/prescriptions/{prescription.Id}");
        await client.PostAsJsonAsync("/api/consultations", new CreateConsultationRequest(appointment.Id, $"Motivo {token}", "x", "y")); // duplicada

        var audit = JsonSerializer.Serialize(await AuditAsync());
        Assert.DoesNotContain(token, audit);
        Assert.Contains("consultations.created", audit);
        Assert.Contains("prescriptions.created", audit);
        // Control: el capturador sí ve lo que se escribe en la bitácora (si no, la ausencia del dato no probaría nada).
        var control = $"CONTROL{Guid.NewGuid():N}";
        factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("control").LogInformation("{Valor}", control);
        Assert.Contains(logs.Messages, m => m.Contains(control));
        Assert.DoesNotContain(logs.Messages, m => m.Contains(token));
    }
}
