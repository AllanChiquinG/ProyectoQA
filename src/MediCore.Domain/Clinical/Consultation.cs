using MediCore.Domain.Doctors;
using MediCore.Domain.Patients;
using MediCore.Domain.Scheduling;

namespace MediCore.Domain.Clinical;

/// <summary>
/// Consulta médica. Pertenece a una cita (una consulta por cita), de la que toma el paciente, el médico y la fecha.
/// Su contenido es información clínica: solo lo lee el médico que la registró y nunca se escribe en bitácoras ni en la auditoría.
/// </summary>
public class Consultation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public Guid AppointmentId { get; set; }
    public Appointment Appointment { get; set; } = null!;

    public required string Reason { get; set; }
    public required string Notes { get; set; }
    public required string Diagnosis { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
