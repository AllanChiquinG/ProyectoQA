using System.Text;
using MediCore.Domain.Messaging;
using MediCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace MediCore.Infrastructure.Messaging;

/// <summary>
/// Publicador del outbox transaccional. El dato clínico y su evento ya se guardaron juntos en PostgreSQL; este proceso en segundo plano
/// publica los eventos pendientes en RabbitMQ con confirmación del broker y recién entonces los marca como procesados. Si el broker
/// falla, el evento sigue pendiente (con espera creciente) y se publica cuando el broker vuelve: nada se pierde. Si se publica pero no
/// llega a marcarse (caída intermedia), se vuelve a publicar: la entrega es "al menos una vez" y el consumidor es idempotente.
/// </summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopes,
    IRabbitMqConnection connection,
    IOptions<MessagingOptions> options,
    TimeProvider time,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.PublishPollInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Nunca se registra el contenido del evento, solo el tipo de error.
                logger.LogWarning("El publicador del outbox no pudo completar el ciclo ({ErrorType}).", ex.GetType().Name);
            }

            try { await Task.Delay(interval, stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Publica los eventos pendientes vencidos. Devuelve cuántos se publicaron. Público para las pruebas.</summary>
    public async Task<int> PublishPendingAsync(CancellationToken ct)
    {
        var settings = options.Value;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
        var now = time.GetUtcNow().UtcDateTime;

        // FOR UPDATE SKIP LOCKED: varias instancias de la API no publican el mismo evento a la vez.
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var batch = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT * FROM outbox_messages
                WHERE "ProcessedAtUtc" IS NULL AND "NextAttemptAtUtc" <= {now}
                ORDER BY "OccurredAtUtc"
                LIMIT {settings.PublishBatchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);
        if (batch.Count == 0) return 0;

        var published = 0;
        IChannel? channel = null;
        try
        {
            foreach (var message in batch)
            {
                try
                {
                    channel ??= await OpenChannelAsync(ct);
                    await PublishAsync(channel, message, ct);
                    message.ProcessedAtUtc = time.GetUtcNow().UtcDateTime;
                    message.LastError = null;
                    published++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                {
                    RegisterFailure(message, ex, settings);
                    logger.LogWarning("No se pudo publicar el evento {MessageId} ({EventType}), intento {Attempts}: {ErrorType}. Sigue pendiente.",
                        message.Id, message.Type, message.Attempts, ex.GetType().Name);
                    if (channel is not null) { await DisposeQuietlyAsync(channel); channel = null; }
                }
            }

            await db.SaveChangesAsync(CancellationToken.None);
            await tx.CommitAsync(CancellationToken.None);
        }
        finally
        {
            if (channel is not null) await DisposeQuietlyAsync(channel);
        }

        if (published > 0) logger.LogInformation("Se publicaron {Count} evento(s) del outbox.", published);
        return published;
    }

    private async Task<IChannel> OpenChannelAsync(CancellationToken ct)
    {
        var conn = await connection.GetAsync(ct);
        var channel = await conn.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await RabbitMqTopologyDeclarer.DeclareAsync(channel, ct);
        return channel;
    }

    private static async Task PublishAsync(IChannel channel, OutboxMessage message, CancellationToken ct)
    {
        var properties = new BasicProperties
        {
            MessageId = message.Id.ToString(),
            Type = message.Type,
            ContentType = "application/json",
            DeliveryMode = DeliveryModes.Persistent,
            Timestamp = new AmqpTimestamp(new DateTimeOffset(DateTime.SpecifyKind(message.OccurredAtUtc, DateTimeKind.Utc)).ToUnixTimeSeconds()),
            Headers = new Dictionary<string, object?> { [Topology.AttemptHeader] = 1 },
        };

        // Con confirmaciones activadas, la tarea termina cuando el broker acepta (o rechaza) el mensaje.
        await channel.BasicPublishAsync(Topology.EventsExchange, message.Type, mandatory: true, properties, Encoding.UTF8.GetBytes(message.Payload), ct);
    }

    private void RegisterFailure(OutboxMessage message, Exception ex, MessagingOptions settings)
    {
        message.Attempts++;
        message.LastError = ex.GetType().Name;
        var seconds = Math.Min(Math.Pow(2, Math.Min(message.Attempts - 1, 10)), settings.PublishMaxBackoff.TotalSeconds);
        message.NextAttemptAtUtc = time.GetUtcNow().UtcDateTime.AddSeconds(seconds);
    }

    private static async Task DisposeQuietlyAsync(IChannel channel)
    {
        try { await channel.DisposeAsync(); } catch { /* el canal ya estaba cerrado */ }
    }
}
