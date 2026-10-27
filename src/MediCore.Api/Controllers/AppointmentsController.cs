using System.Globalization;
using System.Text.RegularExpressions;
using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Contracts.Scheduling;
using MediCore.Domain.Auditing;
using MediCore.Domain.Doctors;
using MediCore.Domain.Patients;
using MediCore.Domain.Scheduling;
using MediCore.Infrastructure.Auditing;
using MediCore.Infrastructure.Persistence;
using MediCore.Infrastructure.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Controllers;

[ApiController]
[Route("api/appointments")]
public partial class AppointmentsController(
    MediCoreDbContext db,
    IAuthorizationService authorization,
    IAvailabilityService availability,
    IClinicClock clock,
    IAuditLogger audit,
    TimeProvider time) : ControllerBase
{
    private const string TimeFormat = "HH:mm";
    private const string SlotTakenMessage = "Este horario ya está ocupado. Selecciona otro para continuar.";

    [GeneratedRegex("^[A-Za-z0-9_-]{8,64}$")]
    private static partial Regex IdempotencyKeyPattern();

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Agenda de un médico en un día (solo citas confirmadas). Recepción elige médico; el médico solo ve la suya.</summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Recepcion},{Roles.Medico}")]
    public async Task<ActionResult<IReadOnlyList<AppointmentDto>>> Agenda([FromQuery] Guid? doctorId, [FromQuery] DateOnly? date, CancellationToken ct)
    {
        if (date is null) return Validation("date", "Indica la fecha (AAAA-MM-DD).");

        var userId = User.UserId();
        if (User.IsInRole(Roles.Medico))
        {
            var ownDoctorId = await db.Doctors.Where(d => d.UserId == userId).Select(d => (Guid?)d.Id).SingleOrDefaultAsync(ct);
            if (doctorId is not null && doctorId != ownDoctorId)
            {
                await audit.WriteAsync(new AuditRecord(AuditActions.AppointmentAccessDenied, AuditOutcome.Failure, userId, nameof(Doctor), doctorId.ToString(), ClientIp, "agenda"), ct);
                return NotFound();
            }

            doctorId = ownDoctorId;
            if (doctorId is null) return new List<AppointmentDto>();
        }
        else if (doctorId is null)
        {
            return Validation("doctorId", "Indica el médico.");
        }

        var dayStart = date.Value.ToDateTime(TimeOnly.MinValue);
        var appointments = await db.Appointments.AsNoTracking().Include(a => a.Patient).Include(a => a.Doctor)
            .Where(a => a.DoctorId == doctorId && a.Status == AppointmentStatus.Confirmed
                        && a.StartsAtLocal >= dayStart && a.StartsAtLocal < dayStart.AddDays(1))
            .OrderBy(a => a.StartsAtLocal)
            .ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.AppointmentAgendaViewed, AuditOutcome.Success, userId, nameof(Doctor), doctorId.ToString(), ClientIp,
            $"count={appointments.Count}"), ct);
        return appointments.Select(ToDto).ToList();
    }

    /// <summary>Citas del paciente autenticado (la app móvil).</summary>
    [HttpGet("me")]
    [Authorize(Roles = Roles.Paciente)]
    public async Task<ActionResult<IReadOnlyList<AppointmentDto>>> Mine(CancellationToken ct)
    {
        var userId = User.UserId();
        var appointments = await db.Appointments.AsNoTracking().Include(a => a.Patient).Include(a => a.Doctor)
            .Where(a => a.Patient.UserId == userId)
            .OrderByDescending(a => a.StartsAtLocal)
            .Take(100)
            .ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.AppointmentAgendaViewed, AuditOutcome.Success, userId, nameof(Appointment), IpAddress: ClientIp,
            Details: $"own;count={appointments.Count}"), ct);
        return appointments.Select(ToDto).ToList();
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Recepcion},{Roles.Medico},{Roles.Paciente}")]
    public async Task<ActionResult<AppointmentDto>> GetById(Guid id, CancellationToken ct)
    {
        var appointment = await LoadAsync(id, ct);
        if (appointment is null) return NotFound();

        if (!(await authorization.AuthorizeAsync(User, appointment, AppointmentOperations.Read)).Succeeded)
            return await DeniedAsync(id, "read", ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.AppointmentRead, AuditOutcome.Success, User.UserId(), nameof(Appointment), id.ToString(), ClientIp), ct);
        return ToDto(appointment);
    }

    /// <summary>
    /// Reserva una cita. Es idempotente: con el encabezado <c>Idempotency-Key</c>, reenviar la misma solicitud devuelve la cita
    /// ya creada. La unicidad del horario la garantiza PostgreSQL (restricciones de exclusión), no esta lógica.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = $"{Roles.Recepcion},{Roles.Paciente}")]
    public async Task<ActionResult<AppointmentDto>> Create(
        CreateAppointmentRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        var userId = User.UserId()!.Value;
        if (idempotencyKey is not null && !IdempotencyKeyPattern().IsMatch(idempotencyKey))
            return Validation("Idempotency-Key", "La clave debe tener entre 8 y 64 caracteres (letras, números, guion y guion bajo).");

        var date = request.Date!.Value;
        var timeOfDay = TimeOnly.ParseExact(request.Time, TimeFormat, CultureInfo.InvariantCulture);

        // 1. Paciente (autorización por recurso: el paciente solo reserva para sí mismo).
        Patient? patient;
        if (User.IsInRole(Roles.Paciente))
        {
            patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, ct);
            if (patient is null) return NotFound();
            if (request.PatientId is not null && request.PatientId != patient.Id)
                return await DeniedAsync(request.PatientId.Value, "create_for_other", ct, nameof(Patient));
        }
        else
        {
            if (request.PatientId is null) return Validation(nameof(request.PatientId), "Selecciona el paciente.");
            patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == request.PatientId, ct);
            if (patient is null) return Problem(statusCode: StatusCodes.Status404NotFound, title: "Paciente no encontrado", detail: "El paciente seleccionado no existe.");
        }

        // 2. Médico, fecha y horario válidos.
        var doctor = await db.Doctors.AsNoTracking().SingleOrDefaultAsync(d => d.Id == request.DoctorId, ct);
        if (doctor is null) return Problem(statusCode: StatusCodes.Status404NotFound, title: "Médico no encontrado", detail: "El médico seleccionado no existe.");

        if (date < clock.Today || date > clock.LastBookableDate)
            return Validation(nameof(request.Date), $"La fecha debe estar entre hoy y el {clock.LastBookableDate:dd/MM/yyyy}.");

        var (start, end) = Appointment.Range(date, timeOfDay);
        if (start <= clock.NowLocal)
            return Validation(nameof(request.Time), "No se puede reservar un horario que ya pasó.");

        var day = await availability.GetAsync(doctor.Id, date, ct);
        if (day.Slots.All(s => s.Time != timeOfDay))
            return Validation(nameof(request.Time), "El horario seleccionado no está dentro de la atención del médico.");

        // 3. Reintento de una solicitud ya procesada.
        if (idempotencyKey is not null && await FindByKeyAsync(userId, idempotencyKey, ct) is { } replay)
            return await ReplayAsync(replay, patient.Id, doctor.Id, start, ct);

        // 4. Alta. PostgreSQL rechaza el solapamiento aunque lleguen miles de solicitudes a la vez.
        var appointment = new Appointment
        {
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            StartsAtLocal = start,
            EndsAtLocal = end,
            Status = AppointmentStatus.Confirmed,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            CreatedByUserId = userId,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        // Las restricciones de exclusión de PostgreSQL pueden terminar en "deadlock" cuando dos inserciones en conflicto
        // llegan a la vez (cada una ve la tupla de la otra sin confirmar). Por eso la alta se serializa con candados de
        // asesoría transaccionales por horario del médico y por paciente: quien llega segundo espera a que el primero
        // confirme y recibe una violación limpia. La restricción sigue siendo la garantía final. Si aun así PostgreSQL
        // aborta por concurrencia, se reintenta.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var doctorLock = $"appointment-doctor:{doctor.Id:N}:{start:yyyyMMddHHmm}";
                var patientLock = $"appointment-patient:{patient.Id:N}";
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({doctorLock}, 0))", ct);
                await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({patientLock}, 0))", ct);

                db.Appointments.Add(appointment);
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
                break;
            }
            catch (DbUpdateException ex) when (ex.IsUniqueViolation() || ex.IsExclusionViolation())
            {
                db.ChangeTracker.Clear();
                return await ResolveConflictAsync(ex, userId, idempotencyKey, patient.Id, doctor.Id, start, ct);
            }
            catch (Exception ex) when (attempt < 3 && ex.IsTransientConcurrencyFailure())
            {
                db.ChangeTracker.Clear();
                await Task.Delay(Random.Shared.Next(10, 60), ct);
            }
        }

        await availability.InvalidateDayAsync(doctor.Id, date, ct);
        await audit.WriteAsync(new AuditRecord(AuditActions.AppointmentCreated, AuditOutcome.Success, userId, nameof(Appointment), appointment.Id.ToString(), ClientIp), ct);

        var created = await LoadAsync(appointment.Id, ct);
        return CreatedAtAction(nameof(GetById), new { id = appointment.Id }, ToDto(created!));
    }

    /// <summary>Cancela una cita y libera el horario. Cancelar una cita ya cancelada no cambia nada.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = $"{Roles.Recepcion},{Roles.Paciente}")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var appointment = await LoadAsync(id, ct);
        if (appointment is null) return NotFound();

        if (!(await authorization.AuthorizeAsync(User, appointment, AppointmentOperations.Cancel)).Succeeded)
            return await DeniedAsync(id, "cancel", ct);

        if (appointment.Status == AppointmentStatus.Cancelled)
            return NoContent();

        if (appointment.StartsAtLocal <= clock.NowLocal)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Cita no cancelable", detail: "No se puede cancelar una cita que ya comenzó o ya pasó.");

        // Actualización condicional y atómica: si dos solicitudes cancelan a la vez, solo una cambia la fila.
        var now = time.GetUtcNow().UtcDateTime;
        var userId = User.UserId();
        var changed = await db.Appointments
            .Where(a => a.Id == id && a.Status == AppointmentStatus.Confirmed)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, AppointmentStatus.Cancelled)
                .SetProperty(a => a.CancelledAtUtc, now)
                .SetProperty(a => a.CancelledByUserId, userId), ct);

        if (changed == 1)
        {
            await availability.InvalidateDayAsync(appointment.DoctorId, DateOnly.FromDateTime(appointment.StartsAtLocal), ct);
            await audit.WriteAsync(new AuditRecord(AuditActions.AppointmentCancelled, AuditOutcome.Success, userId, nameof(Appointment), id.ToString(), ClientIp), ct);
        }

        return NoContent();
    }

    private async Task<ActionResult<AppointmentDto>> ResolveConflictAsync(
        DbUpdateException exception, Guid userId, string? key, Guid patientId, Guid doctorId, DateTime start, CancellationToken ct)
    {
        // a) La misma solicitud llegó dos veces a la vez: gana una y la otra recibe la cita ya creada.
        if (key is not null && await FindByKeyAsync(userId, key, ct) is { } byKey)
            return await ReplayAsync(byKey, patientId, doctorId, start, ct);

        // b) Mismo paciente, médico y hora sin clave: es la misma reserva, no un choque.
        var same = await db.Appointments.AsNoTracking().Include(a => a.Patient).Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.PatientId == patientId && a.DoctorId == doctorId && a.StartsAtLocal == start && a.Status == AppointmentStatus.Confirmed, ct);
        if (same is not null)
            return await ReplayAsync(same, patientId, doctorId, start, ct);

        var patientOverlap = exception.ConstraintName() == SchedulingConstraints.PatientOverlap;
        await audit.WriteAsync(new AuditRecord(AuditActions.AppointmentConflict, AuditOutcome.Failure, userId, nameof(Doctor), doctorId.ToString(), ClientIp,
            patientOverlap ? "patient_overlap" : "doctor_overlap"), ct);

        return Problem(statusCode: StatusCodes.Status409Conflict,
            title: patientOverlap ? "Paciente con otra cita" : "Horario ocupado",
            detail: patientOverlap ? "El paciente ya tiene una cita en ese horario." : SlotTakenMessage);
    }

    private async Task<ActionResult<AppointmentDto>> ReplayAsync(Appointment existing, Guid patientId, Guid doctorId, DateTime start, CancellationToken ct)
    {
        var sameRequest = existing.PatientId == patientId && existing.DoctorId == doctorId && existing.StartsAtLocal == start;
        if (!sameRequest)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Clave de idempotencia en uso",
                detail: "La clave ya se usó con una solicitud distinta. Genera una clave nueva.");

        Response.Headers["Idempotent-Replayed"] = "true";
        await audit.WriteAsync(new AuditRecord(AuditActions.AppointmentReplayed, AuditOutcome.Success, User.UserId(), nameof(Appointment), existing.Id.ToString(), ClientIp), ct);
        return Ok(ToDto(existing));
    }

    private async Task<ActionResult> DeniedAsync(Guid resourceId, string reason, CancellationToken ct, string resourceType = nameof(Appointment))
    {
        await audit.WriteAsync(new AuditRecord(AuditActions.AppointmentAccessDenied, AuditOutcome.Failure, User.UserId(), resourceType, resourceId.ToString(), ClientIp, reason), ct);
        // 404 y no 403: no se confirma a quien no tiene acceso que ese recurso existe.
        return NotFound();
    }

    private Task<Appointment?> FindByKeyAsync(Guid userId, string key, CancellationToken ct)
        => db.Appointments.AsNoTracking().Include(a => a.Patient).Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.CreatedByUserId == userId && a.IdempotencyKey == key, ct);

    private Task<Appointment?> LoadAsync(Guid id, CancellationToken ct)
        => db.Appointments.Include(a => a.Patient).Include(a => a.Doctor).FirstOrDefaultAsync(a => a.Id == id, ct);

    private ActionResult Validation(string field, string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));

    internal static AppointmentDto ToDto(Appointment a) => new(
        a.Id, a.PatientId, a.Patient.FullName, a.DoctorId, a.Doctor.FullName, a.Doctor.Specialty,
        DateOnly.FromDateTime(a.StartsAtLocal), a.StartsAtLocal.ToString(TimeFormat, CultureInfo.InvariantCulture),
        DoctorSchedule.SlotMinutes, a.Status == AppointmentStatus.Cancelled ? AppointmentStatuses.Cancelled : AppointmentStatuses.Confirmed, a.Reason);
}
