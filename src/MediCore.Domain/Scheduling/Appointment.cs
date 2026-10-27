using MediCore.Domain.Doctors;
using MediCore.Domain.Patients;

namespace MediCore.Domain.Scheduling;

public enum AppointmentStatus
{
    Confirmed = 1,
    Cancelled = 2,
}

/// <summary>
/// Cita médica. Fecha y hora son del reloj de la clínica. Que dos citas activas no se solapen (por médico ni por paciente)
/// lo garantiza PostgreSQL con restricciones de exclusión, no el código de la aplicación.
/// </summary>
public class Appointment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;

    public DateTime StartsAtLocal { get; set; }
    public DateTime EndsAtLocal { get; set; }
    public AppointmentStatus Status { get; set; } = AppointmentStatus.Confirmed;
    public string? Reason { get; set; }

    public Guid CreatedByUserId { get; set; }

    /// <summary>Clave enviada por el cliente para que reintentar la misma solicitud no cree otra cita.</summary>
    public string? IdempotencyKey { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAtUtc { get; set; }
    public Guid? CancelledByUserId { get; set; }

    public static (DateTime Start, DateTime End) Range(DateOnly date, TimeOnly time)
    {
        var start = date.ToDateTime(time, DateTimeKind.Unspecified);
        return (start, start.AddMinutes(DoctorSchedule.SlotMinutes));
    }
}
