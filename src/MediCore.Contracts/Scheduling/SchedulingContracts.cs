using System.ComponentModel.DataAnnotations;

namespace MediCore.Contracts.Scheduling;

public static class SchedulingValidation
{
    /// <summary>Hora en formato HH:mm de 24 horas.</summary>
    public const string TimePattern = @"^([01]\d|2[0-3]):[0-5]\d$";
    public const string TimeMessage = "La hora debe tener el formato HH:mm.";
}

public static class AppointmentStatuses
{
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";

    /// <summary>Texto para pantalla, como en el diseño.</summary>
    public static string Mostrar(string status) => status == Cancelled ? "Cancelada" : "Confirmada";
}

/// <summary>Bloque de atención semanal. Los días usan la numeración de <see cref="DayOfWeek"/> (0 = domingo … 6 = sábado).</summary>
public record ScheduleDto(IReadOnlyList<int> Days, string StartTime, string EndTime, int SlotMinutes);

public record ScheduleRequest(
    [Required(ErrorMessage = ValidationMessages.Required)] int[]? Days,
    [Required(ErrorMessage = ValidationMessages.Required), RegularExpression(SchedulingValidation.TimePattern, ErrorMessage = SchedulingValidation.TimeMessage)] string StartTime,
    [Required(ErrorMessage = ValidationMessages.Required), RegularExpression(SchedulingValidation.TimePattern, ErrorMessage = SchedulingValidation.TimeMessage)] string EndTime);

public record SlotDto(string Time, bool Available);

public record AvailabilityDto(Guid DoctorId, DateOnly Date, bool IsWorkingDay, IReadOnlyList<SlotDto> Slots);

public record AppointmentDto(
    Guid Id,
    Guid PatientId,
    string PatientName,
    Guid DoctorId,
    string DoctorName,
    string DoctorSpecialty,
    DateOnly Date,
    string Time,
    int DurationMinutes,
    string Status,
    string? Reason);

public record CreateAppointmentRequest(
    Guid? PatientId,
    [Required(ErrorMessage = ValidationMessages.Required)] Guid? DoctorId,
    [Required(ErrorMessage = ValidationMessages.Required)] DateOnly? Date,
    [Required(ErrorMessage = ValidationMessages.Required), RegularExpression(SchedulingValidation.TimePattern, ErrorMessage = SchedulingValidation.TimeMessage)] string Time,
    [StringLength(120, ErrorMessage = "Máximo 120 caracteres.")] string? Reason = null);
