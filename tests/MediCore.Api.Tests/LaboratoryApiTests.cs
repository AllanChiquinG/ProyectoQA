using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MediCore.Api.Tests.Infrastructure;
using MediCore.Contracts;
using MediCore.Contracts.Audit;
using MediCore.Contracts.Laboratory;
using MediCore.Contracts.Notifications;
using MediCore.Domain.Identity;
using MediCore.Domain.Laboratory;
using MediCore.Domain.Messaging;
using MediCore.Domain.Notifications;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MediCore.Api.Tests;

/// <summary>
/// Laboratorio y notificaciones (sin RabbitMQ: el publicador está apagado, así que el evento queda pendiente en el outbox). Cubre registro, validación,
/// autorización por rol y por recurso, atomicidad resultado + evento, reintento de notificación, API de notificaciones del paciente y ausencia de
/// contenido clínico en auditoría y bitácoras.
/// </summary>
[Trait("Category", "Integration")]
[Collection(StackCollection.Name)]
public class LaboratoryApiTests(StackFixture stack)
{
    private static CreateLabResultRequest NewRequest(Guid patientId, string exam = "Hematología completa", string token = "x")
        => new(patientId, exam, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), $"Observaciones {token}",
            [new LabResultParameterRequest("Hemoglobina", "13.2", "g/dL", "12.0–16.0")]);

    private async Task<(HttpClient Lab, Domain.Patients.Patient Patient)> LabAndPatientAsync(Guid? patientUserId = null)
    {
        var (lab, _) = await stack.LoginClientAsync(UserRole.Laboratorio);
        return (lab, await stack.CreatePatientAsync(patientUserId));
    }

    private static async Task<LabResultDto> RegisterAsync(HttpClient lab, CreateLabResultRequest request)
    {
        var response = await lab.PostAsJsonAsync("/api/labs/results", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LabResultDto>())!;
    }

    private async Task<List<AuditEntryDto>> AuditAsync()
    {
        var (admin, _) = await stack.LoginClientAsync(UserRole.Administrador);
        return (await admin.GetFromJsonAsync<List<AuditEntryDto>>("/api/audit?take=500"))!;
    }

    /// <summary>Médico con cuenta y una cita ya pasada con el paciente (relación válida de atención).</summary>
    private async Task<HttpClient> DoctorWithRelationAsync(Guid patientId)
    {
        var (docUserId, email) = await stack.CreateUserAsync(UserRole.Medico);
        var doctor = await stack.CreateScheduledDoctorAsync(docUserId);
        await stack.CreateAppointmentAsync(patientId, doctor.Id, DateTime.UtcNow.Date.AddDays(-3).AddHours(9));
        return (await stack.LoginClientForAsync(email)).Client;
    }

    [Fact]
    public void Sin_broker_en_estas_pruebas_el_publicador_y_el_consumidor_estan_apagados()
    {
        var hosted = stack.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Select(h => h.GetType().Name).ToList();

        Assert.DoesNotContain(nameof(MediCore.Infrastructure.Messaging.OutboxPublisherService), hosted);
        Assert.DoesNotContain(nameof(MediCore.Infrastructure.Messaging.NotificationConsumerService), hosted);
    }

    // ---- Registro y consulta ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Laboratorio_registra_un_resultado_y_lo_consulta_con_notificacion_pendiente()
    {
        var (lab, patient) = await LabAndPatientAsync();

        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        Assert.Equal(patient.Id, created.PatientId);
        Assert.Equal(NotificationStatuses.Pending, created.NotificationStatus);

        var detail = (await lab.GetFromJsonAsync<LabResultDto>($"/api/labs/results/{created.Id}"))!;
        Assert.Equal("Hematología completa", detail.ExamType);
        var parameter = Assert.Single(detail.Parameters);
        Assert.Equal(("Hemoglobina", "13.2", "g/dL", "12.0–16.0"), (parameter.Name, parameter.Value, parameter.Unit, parameter.ReferenceRange));

        var list = (await lab.GetFromJsonAsync<PagedResult<LabResultSummaryDto>>($"/api/labs/results?q={Uri.EscapeDataString(patient.FullName)}&pageSize=100"))!;
        Assert.Contains(list.Items, r => r.Id == created.Id && r.NotificationStatus == NotificationStatuses.Pending);
    }

    [Fact]
    public async Task Registrar_un_resultado_guarda_en_la_misma_transaccion_el_evento_pendiente_solo_con_identificadores()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id, exam: "ExamenSecreto123"));

        using var scope = stack.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        var id = Assert.Single(await stack.OutboxIdsForLabResultAsync(created.Id));
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        Assert.Null(message.ProcessedAtUtc);
        using var payload = JsonDocument.Parse(message.Payload);
        Assert.Equal(created.Id, payload.RootElement.GetProperty("labResultId").GetGuid());
        Assert.Equal(patient.Id, payload.RootElement.GetProperty("patientId").GetGuid());
        Assert.DoesNotContain("ExamenSecreto123", message.Payload);
    }

    [Fact]
    public async Task Resultado_y_evento_se_guardan_juntos_o_ninguno()
    {
        var patient = await stack.CreatePatientAsync();
        var duplicateId = Guid.NewGuid();
        using (var scope = stack.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
            db.OutboxMessages.Add(new OutboxMessage { Id = duplicateId, Type = "LabResultReady", Payload = "{}" });
            await db.SaveChangesAsync();
        }

        var result = new LabResult { PatientId = patient.Id, RegisteredByUserId = Guid.NewGuid(), ExamType = "Examen atómico", PerformedOn = new DateOnly(2026, 1, 1) };
        using (var scope = stack.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
            db.LabResults.Add(result);
            db.OutboxMessages.Add(new OutboxMessage { Id = duplicateId, Type = "LabResultReady", Payload = "{}" }); // viola la clave: toda la transacción falla
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }

        using var check = stack.Services.CreateScope();
        Assert.False(await check.ServiceProvider.GetRequiredService<MediCoreDbContext>().LabResults.AnyAsync(r => r.Id == result.Id));
    }

    // ---- Validación -------------------------------------------------------------------------------------------------

    public static TheoryData<string, CreateLabResultRequest> InvalidRequests()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var ok = new LabResultParameterRequest("Hemoglobina", "13.2", "g/dL", "12–16");
        var data = new TheoryData<string, CreateLabResultRequest>
        {
            { "sin paciente", new(null, "Examen", today.AddDays(-1), null, [ok]) },
            { "paciente inexistente", new(Guid.NewGuid(), "Examen", today.AddDays(-1), null, [ok]) },
            { "examen vacío", new(Guid.Empty, "  ", today.AddDays(-1), null, [ok]) },
            { "examen demasiado largo", new(Guid.Empty, new string('x', 121), today.AddDays(-1), null, [ok]) },
            { "sin fecha", new(Guid.Empty, "Examen", null, null, [ok]) },
            { "fecha futura", new(Guid.Empty, "Examen", today.AddDays(5), null, [ok]) },
            { "fecha anterior a 2000", new(Guid.Empty, "Examen", new DateOnly(1999, 12, 31), null, [ok]) },
            { "sin parámetros", new(Guid.Empty, "Examen", today.AddDays(-1), null, []) },
            { "once parámetros", new(Guid.Empty, "Examen", today.AddDays(-1), null, Enumerable.Repeat(ok, 11).ToList()) },
            { "parámetro sin nombre", new(Guid.Empty, "Examen", today.AddDays(-1), null, [new("", "1", "u", "r")]) },
            { "parámetro sin valor", new(Guid.Empty, "Examen", today.AddDays(-1), null, [new("Nombre", " ", "u", "r")]) },
            { "valor demasiado largo", new(Guid.Empty, "Examen", today.AddDays(-1), null, [new("Nombre", new string('9', 41), "u", "r")]) },
            { "observaciones demasiado largas", new(Guid.Empty, "Examen", today.AddDays(-1), new string('o', 1001), [ok]) },
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task Entradas_invalidas_se_rechazan_con_400_y_no_se_guarda_nada(string caso, CreateLabResultRequest template)
    {
        var (lab, patient) = await LabAndPatientAsync();
        // Las plantillas con Guid.Empty usan un paciente real para que el único defecto sea el que se prueba.
        var request = template.PatientId == Guid.Empty ? template with { PatientId = patient.Id } : template;

        var response = await lab.PostAsJsonAsync("/api/labs/results", request);

        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"Caso '{caso}': se esperaba 400 y fue {(int)response.StatusCode}.");
        var list = (await lab.GetFromJsonAsync<PagedResult<LabResultSummaryDto>>($"/api/labs/results?q={Uri.EscapeDataString(patient.FullName)}&pageSize=100"))!;
        Assert.DoesNotContain(list.Items, r => r.PatientId == patient.Id);
    }

    [Fact]
    public async Task Unidad_y_rango_son_opcionales()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var request = new CreateLabResultRequest(patient.Id, "Prueba cualitativa", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), null, [new("Embarazo", "Negativo", null, null)]);

        var created = await RegisterAsync(lab, request);

        Assert.Equal("Negativo", Assert.Single(created.Parameters).Value);
        Assert.Equal("", created.Notes);
    }

    // ---- Autorización por rol ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(UserRole.Recepcion)]
    [InlineData(UserRole.Administrador)]
    [InlineData(UserRole.Medico)]
    [InlineData(UserRole.Paciente)]
    public async Task Solo_laboratorio_registra_y_lista_resultados(UserRole role)
    {
        var patient = await stack.CreatePatientAsync();
        var (client, _) = await stack.LoginClientAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/labs/results", NewRequest(patient.Id))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/labs/results")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/labs/patients")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/labs/results/{Guid.NewGuid()}/notification/retry", null)).StatusCode);
    }

    [Fact]
    public async Task Sin_autenticar_no_hay_acceso()
    {
        var anonymous = stack.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/labs/results")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/labs/results", NewRequest(Guid.NewGuid()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"/api/labs/results/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/notifications")).StatusCode);
    }

    [Theory]
    [InlineData(UserRole.Recepcion)]
    [InlineData(UserRole.Administrador)]
    public async Task Recepcion_y_administrador_no_leen_un_resultado_ni_los_de_un_paciente(UserRole role)
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        var (client, _) = await stack.LoginClientAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/labs/results/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/patients/{patient.Id}/lab-results")).StatusCode);
    }

    [Fact]
    public async Task Laboratorio_busca_pacientes_solo_con_nombre_e_identificacion()
    {
        var (lab, patient) = await LabAndPatientAsync();

        var found = (await lab.GetFromJsonAsync<List<LabPatientDto>>($"/api/labs/patients?q={Uri.EscapeDataString(patient.IdentificationNumber)}"))!;

        var dto = Assert.Single(found);
        Assert.Equal(patient.Id, dto.Id);
        var json = JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("phone", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("birth", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Autorización por recurso -----------------------------------------------------------------------------------

    [Fact]
    public async Task Medico_con_relacion_de_atencion_lee_los_resultados_desde_el_expediente()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        var doctor = await DoctorWithRelationAsync(patient.Id);

        var detail = await doctor.GetAsync($"/api/labs/results/{created.Id}");
        var list = (await doctor.GetFromJsonAsync<List<LabResultSummaryDto>>($"/api/patients/{patient.Id}/lab-results"))!;

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains(list, r => r.Id == created.Id);
    }

    [Fact]
    public async Task Medico_sin_relacion_valida_recibe_404_y_queda_auditado()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        var (stranger, _) = await stack.LoginClientAsync(UserRole.Medico);

        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/labs/results/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/api/patients/{patient.Id}/lab-results")).StatusCode);

        var audit = await AuditAsync();
        Assert.Contains(audit, e => e.Action == "clinical.access_denied" && e.ResourceId == created.Id.ToString());
        Assert.Contains(audit, e => e.Action == "clinical.access_denied" && e.ResourceId == patient.Id.ToString());
    }

    [Fact]
    public async Task Medico_pierde_la_lectura_si_se_cancela_su_unica_cita_con_el_paciente()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        var (docUserId, email) = await stack.CreateUserAsync(UserRole.Medico);
        var doctor = await stack.CreateScheduledDoctorAsync(docUserId);
        var appointment = await stack.CreateAppointmentAsync(patient.Id, doctor.Id, DateTime.UtcNow.Date.AddDays(-3).AddHours(9));
        var (client, _) = await stack.LoginClientForAsync(email);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/labs/results/{created.Id}")).StatusCode);

        await stack.SetAppointmentStatusAsync(appointment.Id, Domain.Scheduling.AppointmentStatus.Cancelled);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/labs/results/{created.Id}")).StatusCode);
    }

    [Fact]
    public async Task Paciente_solo_consulta_sus_propios_resultados()
    {
        var (patientUserId, patientEmail) = await stack.CreateUserAsync(UserRole.Paciente);
        var (otherUserId, otherEmail) = await stack.CreateUserAsync(UserRole.Paciente);
        var (lab, own) = await LabAndPatientAsync(patientUserId);
        var other = await stack.CreatePatientAsync(otherUserId);
        var ownResult = await RegisterAsync(lab, NewRequest(own.Id));
        var otherResult = await RegisterAsync(lab, NewRequest(other.Id));
        var (patientClient, _) = await stack.LoginClientForAsync(patientEmail);

        var detail = await patientClient.GetAsync($"/api/labs/results/{ownResult.Id}");
        var foreign = await patientClient.GetAsync($"/api/labs/results/{otherResult.Id}");
        var mine = (await patientClient.GetFromJsonAsync<List<LabResultSummaryDto>>("/api/labs/results/me"))!;

        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(ownResult.Id, Assert.Single(mine).Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await patientClient.GetAsync($"/api/patients/{own.Id}/lab-results")).StatusCode);
        _ = otherEmail;
    }

    [Fact]
    public async Task Paciente_sin_cuenta_vinculada_no_ve_resultados_de_nadie()
    {
        var (lab, patient) = await LabAndPatientAsync(); // paciente sin UserId
        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        var (client, _) = await stack.LoginClientAsync(UserRole.Paciente);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/labs/results/{created.Id}")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<LabResultSummaryDto>>("/api/labs/results/me"))!);
    }

    [Fact]
    public async Task Los_resultados_no_se_modifican_ni_se_borran()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));

        foreach (var method in new[] { HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            var response = await lab.SendAsync(new HttpRequestMessage(method, $"/api/labs/results/{created.Id}") { Content = JsonContent.Create(NewRequest(patient.Id)) });
            Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        }
    }

    // ---- Reintentar notificación ------------------------------------------------------------------------------------

    [Fact]
    public async Task Reintentar_con_el_evento_pendiente_adelanta_su_proximo_intento_sin_duplicarlo()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        using (var scope = stack.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
            var ids = await stack.OutboxIdsForLabResultAsync(created.Id);
            await db.OutboxMessages.Where(m => ids.Contains(m.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextAttemptAtUtc, DateTime.UtcNow.AddHours(1)).SetProperty(m => m.Attempts, 7));
        }

        var response = await lab.PostAsync($"/api/labs/results/{created.Id}/notification/retry", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var check = stack.Services.CreateScope();
        var id = Assert.Single(await stack.OutboxIdsForLabResultAsync(created.Id));
        var message = await check.ServiceProvider.GetRequiredService<MediCoreDbContext>().OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
        Assert.True(message.NextAttemptAtUtc <= DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Reintentar_cuando_el_evento_ya_se_publico_y_no_hay_notificacion_encola_uno_nuevo()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        using (var scope = stack.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
            var ids = await stack.OutboxIdsForLabResultAsync(created.Id);
            await db.OutboxMessages.Where(m => ids.Contains(m.Id)).ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAtUtc, DateTime.UtcNow));
        }

        var response = await lab.PostAsync($"/api/labs/results/{created.Id}/notification/retry", null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var check = stack.Services.CreateScope();
        var pendingIds = await stack.OutboxIdsForLabResultAsync(created.Id);
        var pending = await check.ServiceProvider.GetRequiredService<MediCoreDbContext>().OutboxMessages.AsNoTracking()
            .CountAsync(m => pendingIds.Contains(m.Id) && m.ProcessedAtUtc == null);
        Assert.Equal(1, pending);
    }

    [Fact]
    public async Task Reintentar_si_la_notificacion_ya_existe_no_hace_nada()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        await AddNotificationAsync(patient.Id, created.Id);

        var response = await lab.PostAsync($"/api/labs/results/{created.Id}/notification/retry", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("AlreadySent", (await response.Content.ReadFromJsonAsync<RetryNotificationResponse>())!.Outcome);
        var detail = (await lab.GetFromJsonAsync<LabResultDto>($"/api/labs/results/{created.Id}"))!;
        Assert.Equal(NotificationStatuses.Sent, detail.NotificationStatus);
    }

    [Fact]
    public async Task Reintentar_un_resultado_inexistente_devuelve_404()
    {
        var (lab, _) = await stack.LoginClientAsync(UserRole.Laboratorio);
        Assert.Equal(HttpStatusCode.NotFound, (await lab.PostAsync($"/api/labs/results/{Guid.NewGuid()}/notification/retry", null)).StatusCode);
    }

    // ---- Notificaciones del paciente (API para la app móvil) --------------------------------------------------------

    private async Task<Notification> AddNotificationAsync(Guid patientId, Guid labResultId, bool read = false)
    {
        using var scope = stack.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        var notification = new Notification
        {
            PatientId = patientId, Type = NotificationTypes.LabResultReady, SourceId = labResultId, EventId = Guid.NewGuid(),
            Title = "Tu resultado está disponible", Body = "Ya puedes consultar tu resultado.", ReadAtUtc = read ? DateTime.UtcNow : null,
        };
        db.Notifications.Add(notification);
        await db.SaveChangesAsync();
        return notification;
    }

    [Fact]
    public async Task Paciente_lista_solo_sus_notificaciones_y_las_marca_como_leidas()
    {
        var (userId, email) = await stack.CreateUserAsync(UserRole.Paciente);
        var (otherUserId, otherEmail) = await stack.CreateUserAsync(UserRole.Paciente);
        var patient = await stack.CreatePatientAsync(userId);
        var other = await stack.CreatePatientAsync(otherUserId);
        var mine = await AddNotificationAsync(patient.Id, Guid.NewGuid());
        var read = await AddNotificationAsync(patient.Id, Guid.NewGuid(), read: true);
        var foreign = await AddNotificationAsync(other.Id, Guid.NewGuid());
        var (client, _) = await stack.LoginClientForAsync(email);

        var all = (await client.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/notifications"))!;
        var unread = (await client.GetFromJsonAsync<PagedResult<NotificationDto>>("/api/notifications?unreadOnly=true"))!;
        var count = (await client.GetFromJsonAsync<UnreadCountDto>("/api/notifications/unread-count"))!;

        Assert.Equal(2, all.Total);
        Assert.DoesNotContain(all.Items, n => n.Id == foreign.Id);
        Assert.Equal(mine.Id, Assert.Single(unread.Items).Id);
        Assert.Equal(1, count.Count);
        Assert.Contains(all.Items, n => n.Id == read.Id && n.IsRead);

        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/notifications/{mine.Id}/read", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync($"/api/notifications/{mine.Id}/read", null)).StatusCode); // idempotente
        Assert.Equal(0, (await client.GetFromJsonAsync<UnreadCountDto>("/api/notifications/unread-count"))!.Count);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/api/notifications/{foreign.Id}/read", null)).StatusCode);
        _ = otherEmail;
    }

    [Theory]
    [InlineData(UserRole.Recepcion)]
    [InlineData(UserRole.Administrador)]
    [InlineData(UserRole.Medico)]
    [InlineData(UserRole.Laboratorio)]
    public async Task El_personal_no_accede_a_las_notificaciones_de_pacientes(UserRole role)
    {
        var (client, _) = await stack.LoginClientAsync(role);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/notifications")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/notifications/unread-count")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"/api/notifications/{Guid.NewGuid()}/read", null)).StatusCode);
    }

    [Fact]
    public async Task La_notificacion_unica_por_resultado_y_por_mensaje_la_garantiza_la_base_de_datos()
    {
        var patient = await stack.CreatePatientAsync();
        var labResultId = Guid.NewGuid();
        var first = await AddNotificationAsync(patient.Id, labResultId);

        using var scope = stack.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        db.Notifications.Add(new Notification { PatientId = patient.Id, Type = NotificationTypes.LabResultReady, SourceId = labResultId, EventId = Guid.NewGuid(), Title = "t", Body = "b" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        using var scope2 = stack.Services.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        db2.Notifications.Add(new Notification { PatientId = patient.Id, Type = NotificationTypes.LabResultReady, SourceId = Guid.NewGuid(), EventId = first.EventId, Title = "t", Body = "b" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
    }

    // ---- Auditoría y bitácoras sin contenido clínico ----------------------------------------------------------------

    [Fact]
    public async Task Ni_la_auditoria_ni_las_bitacoras_contienen_el_contenido_del_resultado()
    {
        var logs = new CapturingLoggerProvider();
        var factory = stack.WithWebHostBuilder(b => b.ConfigureLogging(l => { l.SetMinimumLevel(LogLevel.Trace); l.AddProvider(logs); }));
        var token = $"SECRETO{Guid.NewGuid():N}"[..20];
        var (_, labEmail) = await stack.CreateUserAsync(UserRole.Laboratorio);
        var patient = await stack.CreatePatientAsync();
        var lab = factory.CreateClient();
        var login = await StackFixture.LoginAsync(lab, labEmail);
        lab.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", login.AccessToken);

        var created = await RegisterAsync(lab, new CreateLabResultRequest(patient.Id, $"Examen {token}", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), $"Notas {token}",
            [new LabResultParameterRequest($"Parámetro {token}", $"Valor {token}", "u", "r")]));
        await lab.GetAsync($"/api/labs/results/{created.Id}");
        await lab.GetAsync($"/api/labs/results?q={token}");
        await lab.PostAsync($"/api/labs/results/{created.Id}/notification/retry", null);
        await lab.PostAsJsonAsync("/api/labs/results", new CreateLabResultRequest(patient.Id, "", null, null, [])); // inválida

        var audit = JsonSerializer.Serialize(await AuditAsync());
        Assert.DoesNotContain(token, audit);
        Assert.Contains("labs.created", audit);
        Assert.Contains("labs.read", audit);
        Assert.Contains("labs.notification_retried", audit);
        var control = $"CONTROL{Guid.NewGuid():N}";
        factory.Services.GetRequiredService<ILoggerFactory>().CreateLogger("control").LogInformation("{Valor}", control);
        Assert.Contains(logs.Messages, m => m.Contains(control));
        Assert.DoesNotContain(logs.Messages, m => m.Contains(token));
    }
}
