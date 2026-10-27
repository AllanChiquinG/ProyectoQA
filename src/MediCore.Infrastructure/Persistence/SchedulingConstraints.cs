namespace MediCore.Infrastructure.Persistence;

/// <summary>Nombres de las restricciones de la agenda en PostgreSQL (para distinguir qué regla se violó).</summary>
public static class SchedulingConstraints
{
    public const string IdempotencyIndex = "ix_appointments_idempotency";
    public const string DoctorOverlap = "ex_appointments_doctor_overlap";
    public const string PatientOverlap = "ex_appointments_patient_overlap";
}
