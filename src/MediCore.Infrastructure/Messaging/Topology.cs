namespace MediCore.Infrastructure.Messaging;

/// <summary>
/// Nombres de la topología RabbitMQ. Un exchange de eventos (<c>topic</c>) del que cuelga la cola de trabajo del consumidor de
/// notificaciones; los fallos pasan por una cola de reintento con espera (TTL por mensaje, vuelve a la cola de trabajo) y, agotados
/// los intentos, a la cola de mensajes fallidos (DLQ) para revisión.
/// </summary>
public static class Topology
{
    public const string EventsExchange = "medicore.events";
    public const string NotificationsQueue = "medicore.notifications";
    public const string NotificationsRetryQueue = "medicore.notifications.retry";
    public const string NotificationsDeadLetterQueue = "medicore.notifications.dlq";

    public const string AttemptHeader = "x-attempt";
    public const string ErrorHeader = "x-error";
}

/// <summary>Contenido del evento <c>LabResultReady</c>: solo identificadores, sin datos personales ni clínicos.</summary>
public record LabResultReadyEvent(Guid LabResultId, Guid PatientId);

/// <summary>Contenido de <c>AppointmentCreated</c> y <c>AppointmentCancelled</c>: solo identificadores.</summary>
public record AppointmentEvent(Guid AppointmentId, Guid PatientId);

/// <summary>Contenido de <c>PrescriptionCreated</c>: solo identificadores.</summary>
public record PrescriptionCreatedEvent(Guid PrescriptionId, Guid PatientId);

/// <summary>Contenido de <c>PatientNotification</c>: solo identificadores.</summary>
public record PatientNotificationEvent(Guid NotificationId, Guid PatientId);

public static class EventTypes
{
    public const string LabResultReady = "LabResultReady";
    public const string AppointmentCreated = "AppointmentCreated";
    public const string AppointmentCancelled = "AppointmentCancelled";
    public const string PrescriptionCreated = "PrescriptionCreated";
    public const string PatientNotification = "PatientNotification";

    /// <summary>Eventos enlazados a la cola de notificaciones.</summary>
    public static readonly IReadOnlyList<string> All = [LabResultReady, AppointmentCreated, AppointmentCancelled, PrescriptionCreated, PatientNotification];
}
