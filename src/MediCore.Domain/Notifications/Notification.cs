using MediCore.Domain.Patients;

namespace MediCore.Domain.Notifications;

/// <summary>
/// Notificación interna para el paciente (la consulta la app móvil). La genera el consumidor del evento correspondiente; la unicidad
/// de (<see cref="Type"/>, <see cref="SourceId"/>) y de <see cref="EventId"/> garantiza que reprocesar un mensaje no la duplique.
/// </summary>
public class Notification
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    /// <summary>Evento que la originó, por ejemplo <see cref="NotificationTypes.LabResultReady"/>.</summary>
    public required string Type { get; set; }

    /// <summary>Recurso al que apunta (el resultado de laboratorio).</summary>
    public Guid SourceId { get; set; }

    /// <summary>Identificador del mensaje (outbox) que la generó.</summary>
    public Guid EventId { get; set; }

    public required string Title { get; set; }
    public required string Body { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAtUtc { get; set; }
}

public static class NotificationTypes
{
    public const string LabResultReady = "LabResultReady";
}
