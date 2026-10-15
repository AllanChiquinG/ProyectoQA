namespace MediCore.Domain.Messaging;

/// <summary>
/// Evento pendiente de publicar en RabbitMQ. Se guarda en la misma transacción que el dato clínico,
/// de modo que una caída del broker no provoque pérdida de información.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Nombre del evento, por ejemplo <c>AppointmentCreated</c>.</summary>
    public required string Type { get; set; }

    /// <summary>Contenido del evento serializado en JSON.</summary>
    public required string Payload { get; set; }

    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Nulo mientras el evento no se haya publicado.</summary>
    public DateTime? ProcessedAtUtc { get; set; }

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}
