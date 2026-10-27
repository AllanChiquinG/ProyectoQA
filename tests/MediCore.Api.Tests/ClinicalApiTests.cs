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

    // ---- Expediente compartido entre médicos con relación de atención -----------------------------------------------

    /// <summary>Un colega con una cita no cancelada con el mismo paciente (relación de atención válida).</summary>
    private async Task<(HttpClient Client, string Email, Guid DoctorId, Appointment Appointment)> NewColleagueAsync(Guid patientId, int daysAgo = 1)
    {
        var (userId, email) = await stack.CreateUserAsync(UserRole.Medico);
        var doctor = await stack.CreateScheduledDoctorAsync(userId);
        var appointment = await stack.CreateAppointmentAsync(patientId, doctor.Id, DateTime.UtcNow.Date.AddDays(-daysAgo).AddHours(9));
        var (client, _) = await stack.LoginClientForAsync(email);
        return (client, email, doctor.Id, appointment);
    }

    private async Task<(ConsultationDto Consultation, PrescriptionDto Prescription)> WriteClinicalAsync(Scenario author, string token)
    {
        var consultation = await RegisterConsultationAsync(author, token);
        var prescription = (await (await author.Client.PostAsJsonAsync("/api/prescriptions",
            new CreatePrescriptionRequest(consultation.Id, [Item($"Medicamento {token}")]))).Content.ReadFromJsonAsync<PrescriptionDto>())!;
        return (consultation, prescription);
    }

    [Fact]
    public async Task Expediente_compartido_un_medico_con_relacion_lee_las_consultas_y_recetas_de_otros_marcando_cuales_son_suyas()
    {
        var a = await NewScenarioAsync();
        var (consultationA, prescriptionA) = await WriteClinicalAsync(a, "de A");
        var b = await NewColleagueAsync(a.Patient.Id);
        var consultationB = (await (await b.Client.PostAsJsonAsync("/api/consultations", Consultation(b.Appointment.Id, "de B"))).Content.ReadFromJsonAsync<ConsultationDto>())!;

        var seenByB = await b.Client.GetFromJsonAsync<List<ConsultationDto>>($"/api/patients/{a.Patient.Id}/consultations");
        var seenByA = await a.Client.GetFromJsonAsync<List<ConsultationDto>>($"/api/patients/{a.Patient.Id}/consultations");

        Assert.Equal(new[] { consultationA.Id, consultationB.Id }.Order(), seenByB!.Select(c => c.Id).Order());
        Assert.Equal(new[] { consultationA.Id, consultationB.Id }.Order(), seenByA!.Select(c => c.Id).Order());
        Assert.True(seenByB!.Single(c => c.Id == consultationB.Id).IsOwn);
        Assert.False(seenByB!.Single(c => c.Id == consultationA.Id).IsOwn);
        Assert.True(seenByA!.Single(c => c.Id == consultationA.Id).IsOwn);
        Assert.Equal("Diagnóstico de A", seenByB!.Single(c => c.Id == consultationA.Id).Diagnosis); // el contenido se lee completo

        // Lectura individual y recetas.
        var single = await b.Client.GetFromJsonAsync<ConsultationDto>($"/api/consultations/{consultationA.Id}");
        Assert.False(single!.IsOwn);
        var prescriptions = await b.Client.GetFromJsonAsync<List<PrescriptionDto>>($"/api/patients/{a.Patient.Id}/prescriptions");
        var theirs = Assert.Single(prescriptions!);
        Assert.Equal(prescriptionA.Id, theirs.Id);
        Assert.False(theirs.IsOwn);
        var fetched = await b.Client.GetFromJsonAsync<PrescriptionDto>($"/api/prescriptions/{prescriptionA.Id}");
        Assert.Equal("Medicamento de A", Assert.Single(fetched!.Items).Medication);
        Assert.True((await a.Client.GetFromJsonAsync<PrescriptionDto>($"/api/prescriptions/{prescriptionA.Id}"))!.IsOwn);

        // La lectura de registros de otro profesional queda auditada como tal (sin contenido).
        var audit = await AuditAsync();
        Assert.Contains(audit, e => e.Action == "prescriptions.read" && e.ResourceId == prescriptionA.Id.ToString() && e.Details == "other_author");
        Assert.Contains(audit, e => e.Action == "consultations.read" && e.ResourceId == consultationA.Id.ToString() && e.Details == "other_author");
        Assert.Contains(audit, e => e.Action == "consultations.listed" && e.ResourceId == a.Patient.Id.ToString() && e.Details == "count=2;others=1");
    }

    [Fact]
    public async Task Expediente_compartido_ningun_medico_modifica_ni_agrega_a_los_registros_de_otro()
    {
        var a = await NewScenarioAsync();
        var (consultationA, prescriptionA) = await WriteClinicalAsync(a, "de A");
        var b = await NewColleagueAsync(a.Patient.Id);

        // No puede crear una receta sobre la consulta de otro ni registrar la consulta de la cita de otro.
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultationA.Id, [Item()]))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.PostAsJsonAsync("/api/consultations", Consultation(a.Appointment.Id))).StatusCode);
        Assert.Single((await a.Client.GetFromJsonAsync<List<PrescriptionDto>>($"/api/patients/{a.Patient.Id}/prescriptions"))!);

        // No existe ninguna operación para modificar o borrar consultas o recetas, ni para su autor ni para otros.
        foreach (var client in new[] { a.Client, b.Client })
            foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
            {
                Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(new HttpRequestMessage(method, $"/api/consultations/{consultationA.Id}") { Content = JsonContent.Create(new { }) })).StatusCode);
                Assert.Equal(HttpStatusCode.MethodNotAllowed, (await client.SendAsync(new HttpRequestMessage(method, $"/api/prescriptions/{prescriptionA.Id}") { Content = JsonContent.Create(new { }) })).StatusCode);
            }

        var after = await b.Client.GetFromJsonAsync<ConsultationDto>($"/api/consultations/{consultationA.Id}");
        Assert.Equal(("Motivo de A", "Notas de A", "Diagnóstico de A"), (after!.Reason, after.Notes, after.Diagnosis)); // intacta
        Assert.Contains(await AuditAsync(), e => e.Action == "clinical.access_denied" && e.ResourceId == consultationA.Id.ToString());
    }

    [Fact]
    public async Task Expediente_compartido_un_medico_sin_relacion_valida_no_lee_nada_de_un_colega()
    {
        var a = await NewScenarioAsync();
        var (consultationA, prescriptionA) = await WriteClinicalAsync(a, "de A");
        var (userC, emailC) = await stack.CreateUserAsync(UserRole.Medico);
        await stack.CreateScheduledDoctorAsync(userC);
        var (c, login) = await stack.LoginClientForAsync(emailC);

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/patients/{a.Patient.Id}/consultations")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/patients/{a.Patient.Id}/prescriptions")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/consultations/{consultationA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/prescriptions/{prescriptionA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync($"/api/patients/{a.Patient.Id}")).StatusCode);

        var denied = (await AuditAsync()).Where(e => e.Action == "clinical.access_denied" && e.ActorUserId == login.User.Id).Select(e => e.ResourceId).ToList();
        Assert.Contains(consultationA.Id.ToString(), denied);
        Assert.Contains(prescriptionA.Id.ToString(), denied);
        Assert.Contains(a.Patient.Id.ToString(), denied);
    }

    [Fact]
    public async Task Expediente_compartido_la_lectura_depende_de_que_la_relacion_siga_vigente()
    {
        var a = await NewScenarioAsync();
        var (consultationA, prescriptionA) = await WriteClinicalAsync(a, "de A");
        var b = await NewColleagueAsync(a.Patient.Id);
        Assert.Equal(HttpStatusCode.OK, (await b.Client.GetAsync($"/api/prescriptions/{prescriptionA.Id}")).StatusCode);

        await stack.SetAppointmentStatusAsync(b.Appointment.Id, AppointmentStatus.Cancelled); // el colega pierde su única relación

        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.GetAsync($"/api/prescriptions/{prescriptionA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.GetAsync($"/api/consultations/{consultationA.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.GetAsync($"/api/patients/{a.Patient.Id}/consultations")).StatusCode);
        // El autor conserva el acceso: tiene una consulta registrada.
        Assert.Equal(HttpStatusCode.OK, (await a.Client.GetAsync($"/api/patients/{a.Patient.Id}/consultations")).StatusCode);
    }

    [Fact]
    public async Task Expediente_compartido_el_acceso_por_una_consulta_propia_tambien_es_valido()
    {
        var a = await NewScenarioAsync();
        var (_, prescriptionA) = await WriteClinicalAsync(a, "de A");
        var b = await NewColleagueAsync(a.Patient.Id);
        await b.Client.PostAsJsonAsync("/api/consultations", Consultation(b.Appointment.Id, "de B"));
        await stack.SetAppointmentStatusAsync(b.Appointment.Id, AppointmentStatus.Cancelled); // sin cita, pero con consulta propia registrada

        Assert.Equal(HttpStatusCode.OK, (await b.Client.GetAsync($"/api/prescriptions/{prescriptionA.Id}")).StatusCode);
    }

    [Fact]
    public async Task Paciente_ve_sus_recetas_de_todos_sus_medicos_y_nada_mas()
    {
        var (patientUserId, patientEmail) = await stack.CreateUserAsync(UserRole.Paciente);
        var a = await NewScenarioAsync(patientUserId);
        var (_, prescriptionA) = await WriteClinicalAsync(a, "de A");
        var otherPatient = await stack.CreatePatientAsync();
        var b = await NewColleagueAsync(a.Patient.Id);
        var consultationB = (await (await b.Client.PostAsJsonAsync("/api/consultations", Consultation(b.Appointment.Id, "de B"))).Content.ReadFromJsonAsync<ConsultationDto>())!;
        var prescriptionB = (await (await b.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(consultationB.Id, [Item("Medicamento de B")]))).Content.ReadFromJsonAsync<PrescriptionDto>())!;
        var colleagueOfOther = await NewColleagueAsync(otherPatient.Id);
        var otherConsultation = (await (await colleagueOfOther.Client.PostAsJsonAsync("/api/consultations", Consultation(colleagueOfOther.Appointment.Id, "ajena"))).Content.ReadFromJsonAsync<ConsultationDto>())!;
        var foreignPrescription = (await (await colleagueOfOther.Client.PostAsJsonAsync("/api/prescriptions", new CreatePrescriptionRequest(otherConsultation.Id, [Item()]))).Content.ReadFromJsonAsync<PrescriptionDto>())!;
        var (patient, _) = await stack.LoginClientForAsync(patientEmail);

        var mine = await patient.GetFromJsonAsync<List<PrescriptionDto>>("/api/prescriptions/me");

        Assert.Equal(new[] { prescriptionA.Id, prescriptionB.Id }.Order(), mine!.Select(p => p.Id).Order());
        Assert.All(mine!, p => Assert.False(p.IsOwn));
        Assert.Equal(HttpStatusCode.NotFound, (await patient.GetAsync($"/api/prescriptions/{foreignPrescription.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await patient.GetAsync($"/api/patients/{a.Patient.Id}/consultations")).StatusCode);
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
