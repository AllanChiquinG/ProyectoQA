using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace MediCore.Infrastructure.Messaging;

/// <summary>
/// Consumidor de <c>LabResultReady</c>: genera la notificación interna persistida del paciente.
/// <list type="bullet">
/// <item>Confirmación manual: el mensaje se confirma (ack) solo después de guardar la notificación.</item>
/// <item>Duplicados: un mensaje reentregado no crea otra notificación (índices únicos + comprobación previa).</item>
/// <item>Fallos: se reintenta por la cola de reintento con espera; al agotar <see cref="MessagingOptions.ConsumerMaxAttempts"/> pasa a la DLQ.
/// Un mensaje ilegible o imposible de procesar va directo a la DLQ.</item>
/// <item>Sin contenido en bitácoras: solo identificadores, tipo de evento, intento y tipo de error.</item>
/// </list>
/// </summary>
public sealed class NotificationConsumerService(
    IServiceScopeFactory scopes,
    IRabbitMqConnection connection,
    IOptions<MessagingOptions> options,
    ILogger<NotificationConsumerService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            IChannel? channel = null;
            try
            {
                var conn = await connection.GetAsync(stoppingToken);
                channel = await conn.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), stoppingToken);
                await RabbitMqTopologyDeclarer.DeclareAsync(channel, stoppingToken);
                await channel.BasicQosAsync(0, 10, false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, delivery) => OnMessageAsync(channel, delivery, stoppingToken);
                await channel.BasicConsumeAsync(Topology.NotificationsQueue, autoAck: false, consumer, stoppingToken);
                logger.LogInformation("Consumidor de notificaciones activo.");

                // La recuperación automática del cliente restablece conexión y consumidor si el broker se cae y vuelve.
                await Task.Delay(Timeout.Infinite, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning("El consumidor de notificaciones no pudo conectar ({ErrorType}); reintentará.", ex.GetType().Name);
                try { await Task.Delay(options.Value.ConsumerReconnectDelay, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }
            finally
            {
                if (channel is not null)
                {
                    try { await channel.DisposeAsync(); } catch { /* ya cerrado */ }
                }
            }
        }
    }

    private async Task OnMessageAsync(IChannel channel, BasicDeliverEventArgs delivery, CancellationToken ct)
    {
        var settings = options.Value;
        var messageId = Guid.TryParse(delivery.BasicProperties.MessageId, out var id) ? id : Guid.Empty;
        var attempt = ReadAttempt(delivery.BasicProperties);
        try
        {
            if (messageId == Guid.Empty) throw new PermanentMessageException("El mensaje no trae identificador.");
            var evt = ParseEvent(delivery);

            await using var scope = scopes.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetRequiredService<ILabResultReadyHandler>();
            var outcome = await handler.HandleAsync(messageId, evt, ct);

            if (outcome == HandleOutcome.Duplicate)
                logger.LogInformation("Mensaje {MessageId} repetido: la notificación ya existía, se confirma sin crear otra.", messageId);
            else
                logger.LogInformation("Mensaje {MessageId} procesado: notificación generada.", messageId);

            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await HandleFailureAsync(channel, delivery, messageId, attempt, ex, settings, ct);
        }
    }

    private LabResultReadyEvent ParseEvent(BasicDeliverEventArgs delivery)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<LabResultReadyEvent>(Encoding.UTF8.GetString(delivery.Body.Span), Json);
            if (evt is null || evt.LabResultId == Guid.Empty || evt.PatientId == Guid.Empty)
                throw new PermanentMessageException("El mensaje no tiene el formato esperado.");
            return evt;
        }
        catch (JsonException)
        {
            throw new PermanentMessageException("El mensaje no es un JSON válido.");
        }
    }

    private async Task HandleFailureAsync(IChannel channel, BasicDeliverEventArgs delivery, Guid messageId, int attempt, Exception ex, MessagingOptions settings, CancellationToken ct)
    {
        var permanent = ex is PermanentMessageException;
        var toDeadLetter = permanent || attempt >= settings.ConsumerMaxAttempts;

        try
        {
            var headers = new Dictionary<string, object?>(delivery.BasicProperties.Headers ?? new Dictionary<string, object?>())
            {
                [Topology.AttemptHeader] = attempt + 1,
                [Topology.ErrorHeader] = ex.GetType().Name,
            };
            var properties = new BasicProperties(delivery.BasicProperties)
            {
                Headers = headers,
                DeliveryMode = DeliveryModes.Persistent,
                Expiration = toDeadLetter ? null : ((long)settings.ConsumerRetryDelay.TotalMilliseconds).ToString(),
            };

            // Publicación con confirmación y luego ack del original. Si el proceso cae entre ambas, el mensaje se duplica y el handler idempotente lo absorbe.
            await channel.BasicPublishAsync("", toDeadLetter ? Topology.NotificationsDeadLetterQueue : Topology.NotificationsRetryQueue,
                mandatory: true, properties, delivery.Body, ct);
            await channel.BasicAckAsync(delivery.DeliveryTag, false, ct);

            if (toDeadLetter)
                logger.LogError("Mensaje {MessageId} enviado a la cola de mensajes fallidos tras {Attempt} intento(s): {ErrorType}.", messageId, attempt, ex.GetType().Name);
            else
                logger.LogWarning("Falló el mensaje {MessageId} (intento {Attempt} de {Max}): {ErrorType}. Se reintentará.", messageId, attempt, settings.ConsumerMaxAttempts, ex.GetType().Name);
        }
        catch (Exception publishError) when (publishError is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // No se pudo mover el mensaje: se devuelve a la cola para no perderlo.
            logger.LogError("No se pudo mover el mensaje {MessageId} a reintento/DLQ ({ErrorType}); se devuelve a la cola.", messageId, publishError.GetType().Name);
            try { await channel.BasicNackAsync(delivery.DeliveryTag, false, true, ct); } catch { /* el canal cayó: el broker lo reentregará */ }
        }
    }

    private static int ReadAttempt(IReadOnlyBasicProperties properties)
    {
        if (properties.Headers is not null && properties.Headers.TryGetValue(Topology.AttemptHeader, out var value))
        {
            return value switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes when int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) => parsed,
                _ => 1,
            };
        }

        return 1;
    }
}
