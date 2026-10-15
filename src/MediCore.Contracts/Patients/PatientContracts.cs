using System.ComponentModel.DataAnnotations;

namespace MediCore.Contracts.Patients;

public record PatientDto(Guid Id, string FullName, string IdentificationNumber, DateOnly BirthDate, string Phone, string Email);

public record PatientRequest(
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(120, ErrorMessage = "Máximo 120 caracteres.")] string FullName,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(30, ErrorMessage = "Máximo 30 caracteres.")] string IdentificationNumber,
    [Required(ErrorMessage = ValidationMessages.Required), BirthDate] DateOnly? BirthDate,
    [Required(ErrorMessage = ValidationMessages.Required), RegularExpression(ValidationMessages.PhonePattern, ErrorMessage = ValidationMessages.Phone)] string Phone,
    [Required(ErrorMessage = ValidationMessages.Required), EmailAddress(ErrorMessage = ValidationMessages.Email), StringLength(254)] string Email);
