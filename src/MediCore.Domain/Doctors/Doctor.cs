namespace MediCore.Domain.Doctors;

public class Doctor
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string FullName { get; set; }
    public required string Specialty { get; set; }
    public required string Phone { get; set; }
    public required string Email { get; set; }

    /// <summary>Cuenta de acceso del médico (web del personal). Nula si todavía no tiene una.</summary>
    public Guid? UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}
