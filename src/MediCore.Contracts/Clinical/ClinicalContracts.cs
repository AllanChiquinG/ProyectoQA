using System.ComponentModel.DataAnnotations;

namespace MediCore.Contracts.Clinical;

/// <summary>Consulta médica. La fecha es la de la cita a la que pertenece.</summary>
public record ConsultationDto(
    Guid Id,
    Guid PatientId,
    Guid AppointmentId,
    DateOnly Date,
    string DoctorName,
    string Reason,
    string Diagnosis,
    string Notes);

public record CreateConsultationRequest(
    [Required(ErrorMessage = ValidationMessages.Required)] Guid? AppointmentId,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(200, ErrorMessage = "Máximo 200 caracteres.")] string Reason,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(4000, ErrorMessage = "Máximo 4000 caracteres.")] string Notes,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(500, ErrorMessage = "Máximo 500 caracteres.")] string Diagnosis);

/// <summary>Cita del médico con un paciente que todavía no tiene consulta registrada (para elegirla al registrar la consulta).</summary>
public record ConsultableAppointmentDto(Guid Id, DateOnly Date, string Time);

public record PrescriptionItemDto(string Medication, string Dose, string Frequency, string Duration, string Instructions);

public record PrescriptionItemRequest(
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(120, ErrorMessage = "Máximo 120 caracteres.")] string Medication,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(80, ErrorMessage = "Máximo 80 caracteres.")] string Dose,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(80, ErrorMessage = "Máximo 80 caracteres.")] string Frequency,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(80, ErrorMessage = "Máximo 80 caracteres.")] string Duration,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(500, ErrorMessage = "Máximo 500 caracteres.")] string Instructions);

/// <summary>Receta con uno a diez medicamentos, asociada a una consulta del médico que la crea.</summary>
public record CreatePrescriptionRequest(
    [Required(ErrorMessage = ValidationMessages.Required)] Guid? ConsultationId,
    [Required(ErrorMessage = ValidationMessages.Required), MinLength(1, ErrorMessage = "Agrega al menos un medicamento."), MaxLength(10, ErrorMessage = "Una receta admite hasta 10 medicamentos.")]
    List<PrescriptionItemRequest> Items);

public record PrescriptionDto(
    Guid Id,
    Guid PatientId,
    string PatientName,
    Guid DoctorId,
    string DoctorName,
    Guid ConsultationId,
    DateOnly Date,
    IReadOnlyList<PrescriptionItemDto> Items);
