using System.Text.Json;
using MediCore.Domain.Notifications;
using MediCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Infrastructure.Messaging;

/// <summary>Lógica del consumidor de los eventos de citas, recetas y <c>PatientNotification</c>.</summary>
public interface IPatientEventHandler
{
    Task<HandleOutcome> HandleAsync(Guid messageId, string eventType, string body, CancellationToken ct);
}

public sealed class PatientEventHandler(MediCoreDbContext db, TimeProvider time) : IPatientEventHandler
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task<HandleOutcome> HandleAsync(Guid messageId, string eventType, string body, CancellationToken ct) => eventType switch
    {
        EventTypes.AppointmentCreated => AppointmentAsync(messageId, NotificationTypes.AppointmentCreated, Parse<AppointmentEvent>(body), ct),
        EventTypes.AppointmentCancelled => AppointmentAsync(messageId, NotificationTypes.AppointmentCancelled, Parse<AppointmentEvent>(body), ct),
        EventTypes.PrescriptionCreated => PrescriptionAsync(messageId, Parse<PrescriptionCreatedEvent>(body), ct),
        EventTypes.PatientNotification => DispatchAsync(Parse<PatientNotificationEvent>(body), ct),
        _ => throw new PermanentMessageException("El tipo de evento no se reconoce."),
    };

    private async Task<HandleOutcome> AppointmentAsync(Guid messageId, string type, AppointmentEvent message, CancellationToken ct)
    {
        if (message.AppointmentId == Guid.Empty || message.PatientId == Guid.Empty)
            throw new PermanentMessageException("El mensaje no tiene el formato esperado.");
        if (await AlreadyNotifiedAsync(messageId, type, message.AppointmentId, ct)) return HandleOutcome.Duplicate;

        var appointment = await db.Appointments.AsNoTracking()
            .Where(a => a.Id == message.AppointmentId && a.PatientId == message.PatientId)
            .Select(a => new { a.StartsAtLocal, Doctor = a.Doctor.FullName })
            .FirstOrDefaultAsync(ct)
            ?? throw new PermanentMessageException("La cita del evento no existe.");

        var when = $"{appointment.StartsAtLocal:dd/MM/yyyy} a las {appointment.StartsAtLocal:HH:mm}";
        return type == NotificationTypes.AppointmentCreated
            ? await NotifyAsync(messageId, type, message.AppointmentId, message.PatientId, "Tu cita está confirmada", $"Tienes cita con {appointment.Doctor} el {when}.", ct)
            : await NotifyAsync(messageId, type, message.AppointmentId, message.PatientId, "Tu cita fue cancelada", $"Se canceló tu cita con {appointment.Doctor} del {when}.", ct);
    }

    private async Task<HandleOutcome> PrescriptionAsync(Guid messageId, PrescriptionCreatedEvent message, CancellationToken ct)
    {
        if (message.PrescriptionId == Guid.Empty || message.PatientId == Guid.Empty)
            throw new PermanentMessageException("El mensaje no tiene el formato esperado.");
        if (await AlreadyNotifiedAsync(messageId, NotificationTypes.PrescriptionCreated, message.PrescriptionId, ct)) return HandleOutcome.Duplicate;

        var doctor = await db.Prescriptions.AsNoTracking()
            .Where(p => p.Id == message.PrescriptionId && p.PatientId == message.PatientId)
            .Select(p => p.Doctor.FullName)
            .FirstOrDefaultAsync(ct)
            ?? throw new PermanentMessageException("La receta del evento no existe.");

        return await NotifyAsync(messageId, NotificationTypes.PrescriptionCreated, message.PrescriptionId, message.PatientId,
            "Tienes una receta nueva", $"{doctor} registró una receta para ti.", ct);
    }

    private async Task<HandleOutcome> DispatchAsync(PatientNotificationEvent message, CancellationToken ct)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var changed = await db.Notifications
            .Where(n => n.Id == message.NotificationId && n.PatientId == message.PatientId && n.DispatchedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.DispatchedAtUtc, now), ct);
        if (changed == 1) return HandleOutcome.Processed;

        return await db.Notifications.AnyAsync(n => n.Id == message.NotificationId && n.PatientId == message.PatientId, ct)
            ? HandleOutcome.Duplicate
            : throw new PermanentMessageException("La notificación del evento no existe.");
    }

    private Task<bool> AlreadyNotifiedAsync(Guid messageId, string type, Guid sourceId, CancellationToken ct)
        => db.Notifications.AnyAsync(n => (n.Type == type && n.SourceId == sourceId) || n.EventId == messageId, ct);

    private async Task<HandleOutcome> NotifyAsync(Guid messageId, string type, Guid sourceId, Guid patientId, string title, string body, CancellationToken ct)
    {
        var notification = new Notification
        {
            PatientId = patientId,
            Type = type,
            SourceId = sourceId,
            EventId = messageId,
            Title = title,
            Body = body,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Notifications.Add(notification);
        db.OutboxMessages.Add(OutboxEvents.PatientNotification(notification));

        try
        {
            await db.SaveChangesAsync(ct);
            return HandleOutcome.Processed;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return HandleOutcome.Duplicate;
        }
    }

    private static T Parse<T>(string body) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, Json) ?? throw new PermanentMessageException("El mensaje no tiene el formato esperado.");
        }
        catch (JsonException)
        {
            throw new PermanentMessageException("El mensaje no es un JSON válido.");
        }
    }
}
