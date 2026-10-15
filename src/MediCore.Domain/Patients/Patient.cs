namespace MediCore.Domain.Patients;

public class Patient
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FullName { get; set; }
    public required string IdentificationNumber { get; set; }

    /// <summary>Identificación en mayúsculas y sin espacios; es única en el sistema.</summary>
    public required string NormalizedIdentification { get; set; }

    public DateOnly BirthDate { get; set; }
    public required string Phone { get; set; }
    public required string Email { get; set; }

    /// <summary>Cuenta de acceso del paciente (app móvil). Nula si todavía no tiene una.</summary>
    public Guid? UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    public static string NormalizeIdentification(string identification)
        => string.Concat(identification.Where(c => !char.IsWhiteSpace(c))).ToUpperInvariant();
}
