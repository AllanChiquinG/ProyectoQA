using System.ComponentModel.DataAnnotations;

namespace MediCore.Contracts.Laboratory;

public static class NotificationStatuses
{
    /// <summary>La notificación al paciente ya se generó.</summary>
    public const string Sent = "Sent";

    /// <summary>El resultado está guardado, pero la notificación todavía no se generó (broker caído o consumidor con fallos).</summary>
    public const string Pending = "Pending";
}

public record LabResultParameterDto(string Name, string Value, string Unit, string ReferenceRange);

public record LabResultParameterRequest(
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(80, ErrorMessage = "Máximo 80 caracteres.")] string Name,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(40, ErrorMessage = "Máximo 40 caracteres.")] string Value,
    [StringLength(20, ErrorMessage = "Máximo 20 caracteres.")] string? Unit,
    [StringLength(40, ErrorMessage = "Máximo 40 caracteres.")] string? ReferenceRange);

/// <summary>Resultado de uno a diez parámetros para un paciente. Lo registra el rol Laboratorio.</summary>
public record CreateLabResultRequest(
    [Required(ErrorMessage = ValidationMessages.Required)] Guid? PatientId,
    [Required(ErrorMessage = ValidationMessages.Required), StringLength(120, ErrorMessage = "Máximo 120 caracteres.")] string ExamType,
    [Required(ErrorMessage = ValidationMessages.Required)] DateOnly? PerformedOn,
    [StringLength(1000, ErrorMessage = "Máximo 1000 caracteres.")] string? Notes,
    [Required(ErrorMessage = ValidationMessages.Required), MinLength(1, ErrorMessage = "Agrega al menos un parámetro."), MaxLength(10, ErrorMessage = "Un resultado admite hasta 10 parámetros.")]
    List<LabResultParameterRequest> Parameters);

public record LabResultDto(
    Guid Id,
    Guid PatientId,
    string PatientName,
    string ExamType,
    DateOnly Date,
    string Notes,
    IReadOnlyList<LabResultParameterDto> Parameters,
    string NotificationStatus);

public record LabResultSummaryDto(Guid Id, Guid PatientId, string PatientName, string ExamType, DateOnly Date, string NotificationStatus);

/// <summary>Paciente tal como lo ve Laboratorio al elegirlo para registrar un resultado (datos mínimos).</summary>
public record LabPatientDto(Guid Id, string FullName, string IdentificationNumber);

/// <summary>Resultado de pedir de nuevo la notificación: <c>Queued</c> si se reprogramó el envío, <c>AlreadySent</c> si ya existía.</summary>
public record RetryNotificationResponse(string Outcome);
