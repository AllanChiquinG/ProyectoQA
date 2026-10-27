using MediCore.Domain.Notifications;
using MediCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Infrastructure.Messaging;

public enum HandleOutcome
{
    /// <summary>Se generó la notificación.</summary>
    Processed,

    /// <summary>La notificación ya existía (mensaje reentregado o repetido): no se hace nada más.</summary>
    Duplicate,
}

/// <summary>El mensaje no se puede procesar nunca (por ejemplo, el resultado ya no existe): va directo a la cola de mensajes fallidos, sin reintentos.</summary>
public sealed class PermanentMessageException(string message) : Exception(message);

/// <summary>Lógica del consumidor de <c>LabResultReady</c>. Se separa del transporte para probarla y para poder sustituirla en las pruebas de fallos.</summary>
public interface ILabResultReadyHandler
{
    Task<HandleOutcome> HandleAsync(Guid messageId, LabResultReadyEvent message, CancellationToken ct);
}

public sealed class LabResultReadyHandler(MediCoreDbContext db, TimeProvider time) : ILabResultReadyHandler
{
    public async Task<HandleOutcome> HandleAsync(Guid messageId, LabResultReadyEvent message, CancellationToken ct)
    {
        // Idempotencia, primera línea: si ya hay una notificación para este resultado o este mensaje, no se repite.
        if (await db.Notifications.AnyAsync(n => (n.Type == NotificationTypes.LabResultReady && n.SourceId == message.LabResultId) || n.EventId == messageId, ct))
            return HandleOutcome.Duplicate;

        var result = await db.LabResults.AsNoTracking()
            .Where(r => r.Id == message.LabResultId && r.PatientId == message.PatientId)
            .Select(r => new { r.ExamType })
            .FirstOrDefaultAsync(ct)
            ?? throw new PermanentMessageException("El resultado de laboratorio del evento no existe.");

        db.Notifications.Add(new Notification
        {
            PatientId = message.PatientId,
            Type = NotificationTypes.LabResultReady,
            SourceId = message.LabResultId,
            EventId = messageId,
            Title = "Tu resultado está disponible",
            Body = $"Ya puedes consultar tu resultado de {result.ExamType}.",
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            return HandleOutcome.Processed;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Segunda línea: dos consumidores procesaron el mismo mensaje a la vez; la base de datos deja pasar solo uno.
            return HandleOutcome.Duplicate;
        }
    }
}
