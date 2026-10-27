using System.Globalization;
using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Contracts.Scheduling;
using MediCore.Domain.Auditing;
using MediCore.Domain.Doctors;
using MediCore.Domain.Scheduling;
using MediCore.Infrastructure.Auditing;
using MediCore.Infrastructure.Persistence;
using MediCore.Infrastructure.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Controllers;

/// <summary>Horario de atención del médico (WEB-09) y su disponibilidad por fecha.</summary>
[ApiController]
[Route("api/doctors/{doctorId:guid}")]
public class DoctorSchedulesController(MediCoreDbContext db, IAvailabilityService availability, IClinicClock clock, IAuditLogger audit, TimeProvider time) : ControllerBase
{
    private const string TimeFormat = "HH:mm";
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Horarios del médico en una fecha y cuáles están libres. Cacheado en Redis; se invalida al reservar o cancelar.</summary>
    [HttpGet("availability")]
    [Authorize(Roles = $"{Roles.Recepcion},{Roles.Medico},{Roles.Paciente},{Roles.Administrador}")]
    public async Task<ActionResult<AvailabilityDto>> GetAvailability(Guid doctorId, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        if (date is null)
            return Validation("date", "Indica la fecha (AAAA-MM-DD).");
        if (date < clock.Today || date > clock.LastBookableDate)
            return Validation("date", $"La fecha debe estar entre hoy y el {clock.LastBookableDate:dd/MM/yyyy}.");
        if (!await db.Doctors.AnyAsync(d => d.Id == doctorId, ct))
            return NotFound();

        var day = await availability.GetAsync(doctorId, date.Value, ct);
        return new AvailabilityDto(doctorId, date.Value, day.IsWorkingDay,
            day.Slots.Select(s => new SlotDto(s.Time.ToString(TimeFormat, CultureInfo.InvariantCulture), s.Available)).ToList());
    }

    [HttpGet("schedule")]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<ActionResult<ScheduleDto>> GetSchedule(Guid doctorId, CancellationToken ct)
    {
        if (!await db.Doctors.AnyAsync(d => d.Id == doctorId, ct)) return NotFound();

        var schedule = await db.DoctorSchedules.AsNoTracking().SingleOrDefaultAsync(s => s.DoctorId == doctorId, ct);
        return schedule is null ? NotFound() : ToDto(schedule);
    }

    [HttpPut("schedule")]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<ActionResult<ScheduleDto>> PutSchedule(Guid doctorId, ScheduleRequest request, CancellationToken ct)
    {
        if (!await db.Doctors.AnyAsync(d => d.Id == doctorId, ct)) return NotFound();

        var days = request.Days!.Distinct().ToList();
        if (days.Any(d => d is < 0 or > 6))
            return Validation(nameof(request.Days), "Los días de atención no son válidos.");

        var daysOfWeek = days.Select(d => (DayOfWeek)d).ToList();
        var start = TimeOnly.ParseExact(request.StartTime, TimeFormat, CultureInfo.InvariantCulture);
        var end = TimeOnly.ParseExact(request.EndTime, TimeFormat, CultureInfo.InvariantCulture);
        if (DoctorSchedule.Validate(daysOfWeek, start, end) is { } error)
            return Validation(nameof(request.EndTime), error);

        var schedule = await db.DoctorSchedules.SingleOrDefaultAsync(s => s.DoctorId == doctorId, ct);
        if (schedule is null)
        {
            schedule = new DoctorSchedule { DoctorId = doctorId };
            db.DoctorSchedules.Add(schedule);
        }

        schedule.DaysMask = DoctorSchedule.ToMask(daysOfWeek);
        schedule.StartTime = start;
        schedule.EndTime = end;
        schedule.UpdatedAtUtc = time.GetUtcNow().UtcDateTime;

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Dos administradores guardaron el horario del mismo médico a la vez por primera vez.
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Horario en edición", detail: "El horario cambió mientras lo guardabas. Inténtalo de nuevo.");
        }

        // Los horarios cacheados de ese médico ya no son válidos. Las citas ya reservadas se conservan.
        await availability.InvalidateDoctorAsync(doctorId, ct);
        await audit.WriteAsync(new AuditRecord(AuditActions.ScheduleUpdated, AuditOutcome.Success, User.UserId(), nameof(Doctor), doctorId.ToString(), ClientIp), ct);
        return ToDto(schedule);
    }

    private static ScheduleDto ToDto(DoctorSchedule s) => new(
        DoctorSchedule.FromMask(s.DaysMask).Select(d => (int)d).ToList(),
        s.StartTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
        s.EndTime.ToString(TimeFormat, CultureInfo.InvariantCulture),
        DoctorSchedule.SlotMinutes);

    private ActionResult Validation(string field, string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));
}
