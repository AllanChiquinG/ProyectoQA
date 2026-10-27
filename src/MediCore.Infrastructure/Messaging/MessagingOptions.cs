namespace MediCore.Infrastructure.Messaging;

/// <summary>
/// Opciones de mensajería (sección <c>Messaging</c>). La conexión al broker (con credenciales) va en <c>ConnectionStrings:RabbitMq</c>
/// (user-secrets o variable de entorno), nunca en archivos versionados.
/// </summary>
public class MessagingOptions
{
    public const string SectionName = "Messaging";
    public const string ConnectionName = "RabbitMq";

    /// <summary>Activa el publicador del outbox y el consumidor. Las pruebas que no usan RabbitMQ lo apagan.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Cada cuánto busca el publicador eventos pendientes.</summary>
    public TimeSpan PublishPollInterval { get; set; } = TimeSpan.FromSeconds(2);

    public int PublishBatchSize { get; set; } = 20;

    /// <summary>Espera máxima entre reintentos de publicación cuando el broker no responde (crece 1 s, 2 s, 4 s… hasta este tope).</summary>
    public TimeSpan PublishMaxBackoff { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Entregas del consumidor antes de enviar el mensaje a la cola de mensajes fallidos (DLQ).</summary>
    public int ConsumerMaxAttempts { get; set; } = 4;

    /// <summary>Espera antes de reintentar un mensaje que falló en el consumidor.</summary>
    public TimeSpan ConsumerRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Espera antes de reconectar el consumidor cuando el broker no está disponible.</summary>
    public TimeSpan ConsumerReconnectDelay { get; set; } = TimeSpan.FromSeconds(5);
}
