using MediCore.Domain.Doctors;
using MediCore.Domain.Patients;

namespace MediCore.Domain.Clinical;

/// <summary>Receta médica: uno o varios medicamentos indicados en una consulta. No se edita; una corrección es una receta nueva.</summary>
public class Prescription
{
    public const int MaxItems = 10;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public Guid DoctorId { get; set; }
    public Doctor Doctor { get; set; } = null!;
    public Guid ConsultationId { get; set; }
    public Consultation Consultation { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<PrescriptionItem> Items { get; set; } = [];
}

public class PrescriptionItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PrescriptionId { get; set; }

    /// <summary>Orden en que el médico escribió el medicamento.</summary>
    public int Position { get; set; }

    public required string Medication { get; set; }
    public required string Dose { get; set; }
    public required string Frequency { get; set; }
    public required string Duration { get; set; }
    public required string Instructions { get; set; }
}
