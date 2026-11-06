using System.Net;
using System.Net.Http.Json;
using MediCore.Api.Tests.Infrastructure;
using MediCore.Domain.Identity;
using MediCore.Domain.Notifications;
using MediCore.Infrastructure.Messaging;
using MediCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Api.Tests;

/// <summary>Eventos de citas, recetas y <c>PatientNotification</c> por el outbox y RabbitMQ real.</summary>
[Trait("Category", "Integration")]
[Collection(BrokerCollection.Name)]
public class PatientEventsTests(BrokerFixture broker)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    private async Task<T> DbAsync<T>(Func<MediCoreDbContext, Task<T>> query)
    {
        using var scope = broker.Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<MediCoreDbContext>());
    }

    private Task<List<Notification>> NotificationsAsync(Guid sourceId, string type)
        => DbAsync(db => db.Notifications.AsNoTracking().Where(n => n.SourceId == sourceId && n.Type == type).ToListAsync());

    private Task<int> OutboxCountAsync(string type, Guid sourceId)
        => DbAsync(async db => (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Payload).ToListAsync()).Count(p => p.Contains(sourceId.ToString())));

    private static async Task<bool> WaitAsync(Func<Task<bool>> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition()) return true;
            await Task.Delay(150);
        }

        return await condition();
    }

    private async Task<(HttpClient Client, Guid PatientId, Guid AppointmentId)> BookAsync(string time)
    {
        var doctor = await broker.CreateScheduledDoctorAsync(name: "Dra. Eventos");
        var patient = await broker.CreatePatientAsync();
        var (client, _) = await broker.LoginClientAsync(UserRole.Recepcion);
        var response = await client.PostAsJsonAsync("/api/appointments", new { patientId = patient.Id, doctorId = doctor.Id, date = StackFixture.Future(), time, reason = "Control" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<IdOnly>();
        return (client, patient.Id, created!.Id);
    }

    private sealed record IdOnly(Guid Id);

    [Fact]
    public async Task Reservar_una_cita_notifica_al_paciente_una_sola_vez_y_procesa_PatientNotification()
    {
        var (_, patientId, appointmentId) = await BookAsync("09:00");

        Assert.Equal(1, await OutboxCountAsync(EventTypes.AppointmentCreated, appointmentId));
        Assert.True(await WaitAsync(async () => (await NotificationsAsync(appointmentId, NotificationTypes.AppointmentCreated)) is [{ DispatchedAtUtc: not null }]),
            "La notificación no se generó o PatientNotification no se procesó.");

        var notification = Assert.Single(await NotificationsAsync(appointmentId, NotificationTypes.AppointmentCreated));
        Assert.Equal(patientId, notification.PatientId);
        Assert.Contains("Dra. Eventos", notification.Body);
        Assert.DoesNotContain("Control", notification.Body);
    }

    [Fact]
    public async Task Cancelar_una_cita_publica_un_solo_evento_aunque_lleguen_varias_solicitudes()
    {
        var (client, _, appointmentId) = await BookAsync("09:30");

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => client.PostAsync($"/api/appointments/{appointmentId}/cancel", null)));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
        Assert.Equal(1, await OutboxCountAsync(EventTypes.AppointmentCancelled, appointmentId));
        Assert.True(await WaitAsync(async () => (await NotificationsAsync(appointmentId, NotificationTypes.AppointmentCancelled)).Count == 1));
    }

    [Fact]
    public async Task Registrar_una_receta_notifica_al_paciente_sin_contenido_clinico()
    {
        var (doctorUserId, email) = await broker.CreateUserAsync(UserRole.Medico);
        var doctor = await broker.CreateScheduledDoctorAsync(doctorUserId, name: "Dr. Recetas");
        var patient = await broker.CreatePatientAsync();
        var appointment = await broker.CreateAppointmentAsync(patient.Id, doctor.Id, DateTime.UtcNow.Date.AddDays(-3).AddHours(9));
        var consultation = await broker.CreateConsultationAsync(appointment);
        var (client, _) = await broker.LoginClientForAsync(email);

        var response = await client.PostAsJsonAsync("/api/prescriptions", new
        {
            consultationId = consultation.Id,
            items = new[] { new { medication = "Amoxicilina", dose = "500 mg", frequency = "Cada 8 horas", duration = "7 días", instructions = "Con alimentos" } },
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var prescription = await response.Content.ReadFromJsonAsync<IdOnly>();

        Assert.True(await WaitAsync(async () => (await NotificationsAsync(prescription!.Id, NotificationTypes.PrescriptionCreated)).Count == 1));
        var notification = Assert.Single(await NotificationsAsync(prescription!.Id, NotificationTypes.PrescriptionCreated));
        Assert.Contains("Dr. Recetas", notification.Body);
        Assert.DoesNotContain("Amoxicilina", notification.Body);
    }

    [Fact]
    public async Task Con_el_broker_detenido_la_cita_se_guarda_y_la_notificacion_llega_al_volver_el_broker()
    {
        var doctor = await broker.CreateScheduledDoctorAsync();
        var patient = await broker.CreatePatientAsync();
        var (client, _) = await broker.LoginClientAsync(UserRole.Recepcion);
        await broker.StopBrokerAsync();
        Guid appointmentId;
        try
        {
            var response = await client.PostAsJsonAsync("/api/appointments", new { patientId = patient.Id, doctorId = doctor.Id, date = StackFixture.Future(), time = "10:00" });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            appointmentId = (await response.Content.ReadFromJsonAsync<IdOnly>())!.Id;
            Assert.Empty(await NotificationsAsync(appointmentId, NotificationTypes.AppointmentCreated));
        }
        finally
        {
            await broker.StartBrokerAsync();
        }

        Assert.True(await WaitAsync(async () => (await NotificationsAsync(appointmentId, NotificationTypes.AppointmentCreated)).Count == 1, TimeSpan.FromSeconds(60)));
    }

    [Fact]
    public async Task Un_evento_repetido_no_duplica_la_notificacion()
    {
        var (_, patientId, appointmentId) = await BookAsync("10:30");
        Assert.True(await WaitAsync(async () => (await NotificationsAsync(appointmentId, NotificationTypes.AppointmentCreated)).Count == 1));

        using var scope = broker.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IPatientEventHandler>();
        var body = $"{{\"appointmentId\":\"{appointmentId}\",\"patientId\":\"{patientId}\"}}";
        var outcome = await handler.HandleAsync(Guid.NewGuid(), EventTypes.AppointmentCreated, body, CancellationToken.None);

        Assert.Equal(HandleOutcome.Duplicate, outcome);
        Assert.Single(await NotificationsAsync(appointmentId, NotificationTypes.AppointmentCreated));
    }

    [Fact]
    public async Task Un_evento_desconocido_o_de_una_cita_inexistente_es_un_fallo_permanente()
    {
        using var scope = broker.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<IPatientEventHandler>();
        var missing = $"{{\"appointmentId\":\"{Guid.NewGuid()}\",\"patientId\":\"{Guid.NewGuid()}\"}}";

        await Assert.ThrowsAsync<PermanentMessageException>(() => handler.HandleAsync(Guid.NewGuid(), EventTypes.AppointmentCreated, missing, CancellationToken.None));
        await Assert.ThrowsAsync<PermanentMessageException>(() => handler.HandleAsync(Guid.NewGuid(), "EventoInventado", "{}", CancellationToken.None));
    }
}
