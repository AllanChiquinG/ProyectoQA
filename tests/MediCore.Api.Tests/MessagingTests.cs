using System.Net;
using System.Net.Http.Json;
using System.Text;
using MediCore.Api.Tests.Infrastructure;
using MediCore.Contracts;
using MediCore.Contracts.Laboratory;
using MediCore.Domain.Identity;
using MediCore.Domain.Notifications;
using MediCore.Infrastructure.Messaging;
using MediCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace MediCore.Api.Tests;

/// <summary>
/// RabbitMQ real (Testcontainers) con outbox transaccional: publicación, consumo, broker caído, recuperación, reentrega sin duplicados,
/// reintentos y cola de mensajes fallidos.
/// </summary>
[Trait("Category", "Integration")]
[Collection(BrokerCollection.Name)]
public class MessagingTests(BrokerFixture broker)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    private static CreateLabResultRequest NewRequest(Guid patientId, string exam = "Química sanguínea")
        => new(patientId, exam, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1), "Observaciones",
            [new LabResultParameterRequest("Glucosa", "92", "mg/dL", "70–100")]);

    private async Task<(HttpClient Lab, Domain.Patients.Patient Patient)> LabAndPatientAsync()
    {
        var (lab, _) = await broker.LoginClientAsync(UserRole.Laboratorio);
        return (lab, await broker.CreatePatientAsync());
    }

    private static async Task<LabResultDto> RegisterAsync(HttpClient lab, CreateLabResultRequest request)
    {
        var response = await lab.PostAsJsonAsync("/api/labs/results", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<LabResultDto>())!;
    }

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

    private async Task<List<Notification>> NotificationsAsync(Guid labResultId)
    {
        using var scope = broker.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MediCoreDbContext>().Notifications.AsNoTracking()
            .Where(n => n.SourceId == labResultId).ToListAsync();
    }

    private async Task<Domain.Messaging.OutboxMessage> OutboxAsync(Guid labResultId)
    {
        var id = (await broker.OutboxIdsForLabResultAsync(labResultId)).First();
        using var scope = broker.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MediCoreDbContext>().OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    /// <summary>Lee (y retira) un mensaje de la cola de mensajes fallidos que corresponda al resultado.</summary>
    private async Task<(bool Found, string? Error, int Attempt)> TakeFromDeadLetterAsync(Guid labResultId)
    {
        var factory = new ConnectionFactory { Uri = new Uri(broker.BrokerConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        for (var i = 0; i < 200; i++)
        {
            var message = await channel.BasicGetAsync(Topology.NotificationsDeadLetterQueue, autoAck: false);
            if (message is null) return (false, null, 0);
            var body = Encoding.UTF8.GetString(message.Body.Span);
            if (body.Contains(labResultId.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                await channel.BasicAckAsync(message.DeliveryTag, false);
                var headers = message.BasicProperties.Headers!;
                var error = headers.TryGetValue(Topology.ErrorHeader, out var e) ? Encoding.UTF8.GetString((byte[])e!) : null;
                var attempt = headers.TryGetValue(Topology.AttemptHeader, out var a) ? Convert.ToInt32(a) : 0;
                return (true, error, attempt);
            }

            // Los que no son de este resultado se dejan sin confirmar: vuelven a la cola cuando se cierra el canal.
        }

        return (false, null, 0);
    }

    private async Task PublishRawAsync(string messageId, string body, string routingKey = EventTypes.LabResultReady)
    {
        var factory = new ConnectionFactory { Uri = new Uri(broker.BrokerConnectionString) };
        await using var connection = await factory.CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true));
        await channel.BasicPublishAsync(Topology.EventsExchange, routingKey, mandatory: true,
            new BasicProperties { MessageId = messageId, Type = routingKey, DeliveryMode = DeliveryModes.Persistent, Headers = new Dictionary<string, object?> { [Topology.AttemptHeader] = 1 } },
            Encoding.UTF8.GetBytes(body));
    }

    [Fact]
    public void Con_broker_el_publicador_y_el_consumidor_estan_activos()
    {
        var hosted = broker.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().Select(h => h.GetType().Name).ToList();

        Assert.Contains(nameof(OutboxPublisherService), hosted);
        Assert.Contains(nameof(NotificationConsumerService), hosted);
    }

    // ---- Flujo normal -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Resultado_registrado_se_publica_se_consume_y_genera_la_notificacion_interna_del_paciente()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));

        Assert.True(await WaitAsync(async () => (await NotificationsAsync(created.Id)).Count == 1), "No se generó la notificación a tiempo.");

        var notification = Assert.Single(await NotificationsAsync(created.Id));
        Assert.Equal(patient.Id, notification.PatientId);
        Assert.Equal(NotificationTypes.LabResultReady, notification.Type);
        Assert.Contains("Química sanguínea", notification.Body);
        var outbox = await OutboxAsync(created.Id);
        Assert.Equal(outbox.Id, notification.EventId);
        Assert.True(await WaitAsync(async () => (await OutboxAsync(created.Id)).ProcessedAtUtc is not null));
        var detail = (await lab.GetFromJsonAsync<LabResultDto>($"/api/labs/results/{created.Id}"))!;
        Assert.Equal(NotificationStatuses.Sent, detail.NotificationStatus);
    }

    // ---- Broker caído y recuperación --------------------------------------------------------------------------------

    [Fact]
    public async Task Con_el_broker_detenido_el_resultado_se_guarda_y_el_evento_se_publica_al_volver_el_broker()
    {
        var (lab, patient) = await LabAndPatientAsync();
        await broker.StopBrokerAsync();
        LabResultDto created;
        try
        {
            created = await RegisterAsync(lab, NewRequest(patient.Id, "Examen con broker caído"));

            // El resultado está guardado y se puede leer; la notificación sigue pendiente y el evento reintenta con espera creciente.
            var detail = (await lab.GetFromJsonAsync<LabResultDto>($"/api/labs/results/{created.Id}"))!;
            Assert.Equal("Examen con broker caído", detail.ExamType);
            Assert.Equal(NotificationStatuses.Pending, detail.NotificationStatus);
            Assert.True(await WaitAsync(async () => (await OutboxAsync(created.Id)).Attempts >= 1, TimeSpan.FromSeconds(30)), "El publicador no registró el intento fallido.");
            var pending = await OutboxAsync(created.Id);
            Assert.Null(pending.ProcessedAtUtc);
            Assert.Empty(await NotificationsAsync(created.Id));
        }
        finally
        {
            await broker.StartBrokerAsync();
        }

        Assert.True(await WaitAsync(async () => (await NotificationsAsync(created.Id)).Count == 1, TimeSpan.FromSeconds(60)), "El evento pendiente no se recuperó al volver el broker.");
        var recovered = await OutboxAsync(created.Id);
        Assert.NotNull(recovered.ProcessedAtUtc);
        Assert.True(recovered.Attempts >= 1);
        Assert.Equal(NotificationStatuses.Sent, (await lab.GetFromJsonAsync<LabResultDto>($"/api/labs/results/{created.Id}"))!.NotificationStatus);
    }

    // ---- Duplicados -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Un_mensaje_reentregado_no_duplica_la_notificacion()
    {
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        Assert.True(await WaitAsync(async () => (await NotificationsAsync(created.Id)).Count == 1));
        var original = await OutboxAsync(created.Id);

        // Reentrega del MISMO mensaje (mismo MessageId) y de un mensaje nuevo para el mismo resultado: ambos son duplicados.
        await PublishRawAsync(original.Id.ToString(), original.Payload);
        await PublishRawAsync(original.Id.ToString(), original.Payload);
        await PublishRawAsync(Guid.NewGuid().ToString(), original.Payload);
        var sentinel = await RegisterAsync(lab, NewRequest(patient.Id, "Centinela"));

        // Cuando se procesó el centinela (que llegó después), los duplicados anteriores ya se consumieron.
        Assert.True(await WaitAsync(async () => (await NotificationsAsync(sentinel.Id)).Count == 1));
        await Task.Delay(500);
        var notifications = await NotificationsAsync(created.Id);
        Assert.Single(notifications);
        Assert.Equal(original.Id, notifications[0].EventId);
    }

    [Fact]
    public async Task Publicar_dos_veces_el_mismo_evento_del_outbox_deja_una_sola_notificacion()
    {
        // Simula la caída entre "publicado" y "marcado como procesado": el evento se vuelve a publicar.
        var (lab, patient) = await LabAndPatientAsync();
        var created = await RegisterAsync(lab, NewRequest(patient.Id));
        Assert.True(await WaitAsync(async () => (await NotificationsAsync(created.Id)).Count == 1));
        Assert.True(await WaitAsync(async () => (await OutboxAsync(created.Id)).ProcessedAtUtc is not null));
        var message = await OutboxAsync(created.Id);
        using (var scope = broker.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<MediCoreDbContext>().OutboxMessages.Where(m => m.Id == message.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.ProcessedAtUtc, (DateTime?)null).SetProperty(m => m.NextAttemptAtUtc, DateTime.UtcNow));
        }

        Assert.True(await WaitAsync(async () => (await OutboxAsync(created.Id)).ProcessedAtUtc is not null));
        var sentinel = await RegisterAsync(lab, NewRequest(patient.Id, "Centinela 2"));
        Assert.True(await WaitAsync(async () => (await NotificationsAsync(sentinel.Id)).Count == 1));
        await Task.Delay(500);

        Assert.Single(await NotificationsAsync(created.Id));
    }

    // ---- Fallos del consumidor --------------------------------------------------------------------------------------

    [Fact]
    public async Task Si_el_consumidor_falla_dos_veces_el_mensaje_se_reintenta_y_termina_procesado()
    {
        var (lab, patient) = await LabAndPatientAsync();
        broker.Faults.FailTimes(patient.Id, 2); // los fallos se arman antes de registrar el resultado: no hay carrera con el publicador
        var created = await RegisterAsync(lab, NewRequest(patient.Id));

        Assert.True(await WaitAsync(async () => (await NotificationsAsync(created.Id)).Count == 1), "El mensaje no se procesó tras los reintentos.");
        Assert.Equal(3, broker.Faults.Calls(patient.Id));
        Assert.Single(await NotificationsAsync(created.Id));
        Assert.False((await TakeFromDeadLetterAsync(created.Id)).Found);
    }

    [Fact]
    public async Task Si_el_consumidor_falla_siempre_el_mensaje_va_a_la_cola_de_mensajes_fallidos_sin_notificacion()
    {
        var (lab, patient) = await LabAndPatientAsync();
        broker.Faults.FailTimes(patient.Id, int.MaxValue);
        var created = await RegisterAsync(lab, NewRequest(patient.Id));

        (bool Found, string? Error, int Attempt) dead = default;
        Assert.True(await WaitAsync(async () => (dead = await TakeFromDeadLetterAsync(created.Id)).Found), "El mensaje no llegó a la cola de mensajes fallidos.");

        Assert.Equal("InvalidOperationException", dead.Error); // solo el tipo de error, nunca contenido
        Assert.Equal(4, dead.Attempt); // 3 intentos configurados; el contador ya apunta al siguiente
        Assert.Equal(3, broker.Faults.Calls(patient.Id));
        Assert.Empty(await NotificationsAsync(created.Id));
        Assert.Equal(NotificationStatuses.Pending, (await lab.GetFromJsonAsync<LabResultDto>($"/api/labs/results/{created.Id}"))!.NotificationStatus);

        // Se corrige la causa y Laboratorio reintenta desde WEB-18: se encola un evento nuevo y la notificación se genera una sola vez.
        broker.Faults.StopFailing(patient.Id);
        var retry = await lab.PostAsync($"/api/labs/results/{created.Id}/notification/retry", null);
        Assert.Equal(HttpStatusCode.Accepted, retry.StatusCode);
        Assert.True(await WaitAsync(async () => (await NotificationsAsync(created.Id)).Count == 1));
        Assert.Equal(NotificationStatuses.Sent, (await lab.GetFromJsonAsync<LabResultDto>($"/api/labs/results/{created.Id}"))!.NotificationStatus);
    }

    [Fact]
    public async Task Un_mensaje_ilegible_o_de_un_resultado_inexistente_va_directo_a_la_cola_de_mensajes_fallidos()
    {
        var unknown = Guid.NewGuid();
        await PublishRawAsync(Guid.NewGuid().ToString(), $"{{\"labResultId\":\"{unknown}\",\"patientId\":\"{Guid.NewGuid()}\"}}");
        var garbageMarker = Guid.NewGuid();
        await PublishRawAsync(Guid.NewGuid().ToString(), $"esto no es json {garbageMarker}");

        (bool Found, string? Error, int Attempt) missing = default, garbage = default;
        Assert.True(await WaitAsync(async () => (missing = await TakeFromDeadLetterAsync(unknown)).Found));
        Assert.True(await WaitAsync(async () => (garbage = await TakeFromDeadLetterAsync(garbageMarker)).Found));

        Assert.Equal("PermanentMessageException", missing.Error);
        Assert.Equal("PermanentMessageException", garbage.Error);
        Assert.Equal(2, missing.Attempt); // sin reintentos: un solo intento
        Assert.Empty(await NotificationsAsync(unknown));
    }
}
