using System.Text.Json;
using MediCore.Domain.Messaging;
using MediCore.Domain.Notifications;

namespace MediCore.Infrastructure.Messaging;

/// <summary>Crea los eventos del outbox. Llevan solo identificadores, nunca datos personales ni clínicos.</summary>
public static class OutboxEvents
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static OutboxMessage AppointmentCreated(Guid appointmentId, Guid patientId, DateTime nowUtc)
        => Create(EventTypes.AppointmentCreated, new AppointmentEvent(appointmentId, patientId), nowUtc);

    public static OutboxMessage AppointmentCancelled(Guid appointmentId, Guid patientId, DateTime nowUtc)
        => Create(EventTypes.AppointmentCancelled, new AppointmentEvent(appointmentId, patientId), nowUtc);

    public static OutboxMessage PrescriptionCreated(Guid prescriptionId, Guid patientId, DateTime nowUtc)
        => Create(EventTypes.PrescriptionCreated, new PrescriptionCreatedEvent(prescriptionId, patientId), nowUtc);

    public static OutboxMessage PatientNotification(Notification notification)
        => Create(EventTypes.PatientNotification, new PatientNotificationEvent(notification.Id, notification.PatientId), notification.CreatedAtUtc);

    private static OutboxMessage Create<T>(string type, T payload, DateTime nowUtc) => new()
    {
        Type = type,
        Payload = JsonSerializer.Serialize(payload, Json),
        OccurredAtUtc = nowUtc,
        NextAttemptAtUtc = nowUtc,
    };
}
