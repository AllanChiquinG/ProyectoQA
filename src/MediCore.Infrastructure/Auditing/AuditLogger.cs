using MediCore.Domain.Auditing;
using MediCore.Infrastructure.Persistence;

namespace MediCore.Infrastructure.Auditing;

public record AuditRecord(
    string Action,
    string Outcome,
    Guid? ActorUserId = null,
    string? ResourceType = null,
    string? ResourceId = null,
    string? IpAddress = null,
    string? Details = null);

public interface IAuditLogger
{
    Task WriteAsync(AuditRecord record, CancellationToken ct = default);
}

internal sealed class AuditLogger(MediCoreDbContext db, TimeProvider clock) : IAuditLogger
{
    public async Task WriteAsync(AuditRecord record, CancellationToken ct = default)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
            ActorUserId = record.ActorUserId,
            Action = record.Action,
            ResourceType = record.ResourceType,
            ResourceId = record.ResourceId,
            Outcome = record.Outcome,
            IpAddress = record.IpAddress,
            Details = record.Details,
        });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Acciones auditadas por el módulo de identidad.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "auth.login";
    public const string LoginFailed = "auth.login_failed";
    public const string Logout = "auth.logout";
    public const string RefreshReuseDetected = "auth.refresh_reuse";
    public const string UserRoleChanged = "users.role_changed";
    public const string AuditViewed = "audit.viewed";

    public const string PatientCreated = "patients.created";
    public const string PatientUpdated = "patients.updated";
    public const string PatientRead = "patients.read";
    public const string PatientSearched = "patients.searched";
    public const string PatientAccessDenied = "patients.access_denied";
    public const string DoctorCreated = "doctors.created";
    public const string DoctorUpdated = "doctors.updated";

    public const string ScheduleUpdated = "schedules.updated";
    public const string AppointmentCreated = "appointments.created";
    public const string AppointmentReplayed = "appointments.replayed";
    public const string AppointmentConflict = "appointments.conflict";
    public const string AppointmentCancelled = "appointments.cancelled";
    public const string AppointmentRead = "appointments.read";
    public const string AppointmentAgendaViewed = "appointments.agenda_viewed";
    public const string AppointmentAccessDenied = "appointments.access_denied";

    // Información clínica: la auditoría registra QUIÉN, QUÉ recurso y CUÁNDO; nunca el contenido (motivo, notas, diagnóstico, medicamentos).
    public const string ConsultationCreated = "consultations.created";
    public const string ConsultationsListed = "consultations.listed";
    public const string ConsultationRead = "consultations.read";
    public const string PrescriptionCreated = "prescriptions.created";
    public const string PrescriptionRead = "prescriptions.read";
    public const string PrescriptionsListed = "prescriptions.listed";
    public const string ClinicalAccessDenied = "clinical.access_denied";
}
