using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace MediCore.Infrastructure.Messaging;

/// <summary>Conexión compartida y perezosa al broker. Si el broker no está disponible lanza la excepción de RabbitMQ al usarse.</summary>
public interface IRabbitMqConnection
{
    Task<IConnection> GetAsync(CancellationToken ct);
}

internal sealed class RabbitMqConnection(IConfiguration configuration, ILogger<RabbitMqConnection> logger) : IRabbitMqConnection, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IConnection? _connection;

    public async Task<IConnection> GetAsync(CancellationToken ct)
    {
        if (_connection is { IsOpen: true }) return _connection;

        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is { IsOpen: true }) return _connection;

            // Una conexión que ya existió se recupera sola (recuperación automática del cliente, que también restablece el consumidor).
            // No se reemplaza: cerrarla mientras se recupera dejaría al consumidor sin su canal.
            if (_connection is not null)
                throw new InvalidOperationException("La conexión con RabbitMQ no está disponible; se está recuperando.");

            var uri = configuration.GetConnectionString(MessagingOptions.ConnectionName)
                ?? throw new InvalidOperationException(
                    "Falta la cadena de conexión 'ConnectionStrings:RabbitMq'. Defínela con user-secrets o con la variable de entorno ConnectionStrings__RabbitMq (ver README).");
            var factory = new ConnectionFactory
            {
                Uri = new Uri(uri),
                ClientProvidedName = "medicore",
                AutomaticRecoveryEnabled = true,
                TopologyRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(2),
                RequestedConnectionTimeout = TimeSpan.FromSeconds(3),
            };

            _connection = await factory.CreateConnectionAsync(ct);
            logger.LogInformation("Conectado a RabbitMQ.");
            return _connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task SafeDisposeAsync(IConnection connection)
    {
        try { await connection.DisposeAsync(); } catch { /* ya estaba cerrada */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null) await SafeDisposeAsync(_connection);
        _gate.Dispose();
    }
}

internal static class RabbitMqTopologyDeclarer
{
    /// <summary>Declara exchange, colas y enlaces (idempotente). Los argumentos no cambian entre ejecuciones para no chocar con colas existentes.</summary>
    public static async Task DeclareAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Topology.EventsExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.QueueDeclareAsync(Topology.NotificationsQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(Topology.NotificationsQueue, Topology.EventsExchange, EventTypes.LabResultReady, cancellationToken: ct);

        // Cola de reintento: cada mensaje lleva su propia espera (Expiration) y, al vencer, vuelve a la cola de trabajo por el exchange por defecto.
        await channel.QueueDeclareAsync(Topology.NotificationsRetryQueue, durable: true, exclusive: false, autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = "",
                ["x-dead-letter-routing-key"] = Topology.NotificationsQueue,
            }, cancellationToken: ct);

        await channel.QueueDeclareAsync(Topology.NotificationsDeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
    }
}
