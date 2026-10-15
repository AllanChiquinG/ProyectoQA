using System.ComponentModel.DataAnnotations;

namespace MediCore.Contracts.Doctors;

/// <summary>Teléfono y correo son <c>null</c> para el rol Paciente (solo ve nombre y especialidad).</summary>
public record DoctorDto(Guid Id, string FullName, string Specialty, string? Phone, string? Email);

public record DoctorRequest(
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(120, ErrorMessage = "Máximo 120 caracteres.")] string FullName,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(80, ErrorMessage = "Máximo 80 caracteres.")] string Specialty,
    [Required(ErrorMessage = ValidationMessages.Required), RegularExpression(ValidationMessages.PhonePattern, ErrorMessage = ValidationMessages.Phone)] string Phone,
    [Required(ErrorMessage = ValidationMessages.Required), EmailAddress(ErrorMessage = ValidationMessages.Email), StringLength(254)] string Email);
