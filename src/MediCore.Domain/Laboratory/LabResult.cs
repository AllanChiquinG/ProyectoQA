using MediCore.Domain.Patients;

namespace MediCore.Domain.Laboratory;

/// <summary>Resultado de laboratorio de un paciente con uno o varios parámetros. No se edita; una corrección es un resultado nuevo.</summary>
public class LabResult
{
    public const int MaxParameters = 10;

    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PatientId { get; set; }
    public Patient Patient { get; set; } = null!;

    /// <summary>Usuario de Laboratorio que registró el resultado.</summary>
    public Guid RegisteredByUserId { get; set; }

    public required string ExamType { get; set; }

    /// <summary>Fecha en que se realizó el examen (calendario de la clínica).</summary>
    public DateOnly PerformedOn { get; set; }

    public string Notes { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public List<LabResultParameter> Parameters { get; set; } = [];
}

public class LabResultParameter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LabResultId { get; set; }
    public int Position { get; set; }
    public required string Name { get; set; }
    public required string Value { get; set; }
    public string Unit { get; set; } = "";
    public string ReferenceRange { get; set; } = "";
}
