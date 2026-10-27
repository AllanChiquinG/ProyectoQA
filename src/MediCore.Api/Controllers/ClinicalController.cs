using System.Globalization;
using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Contracts.Clinical;
using MediCore.Domain.Auditing;
using MediCore.Domain.Clinical;
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

/// <summary>
/// Expediente clínico compartido entre médicos con relación de atención: consultas y recetas. Información sensible: se entrega solo a quien corresponde y la auditoría registra
/// quién accedió a qué recurso y cuándo, nunca el contenido (motivo, notas, diagnóstico ni medicamentos).
/// </summary>
[ApiController]
[Route("api")]
public class ClinicalController(
    MediCoreDbContext db,
    IAuthorizationService authorization,
    IAuditLogger audit,
    IClinicClock clock,
    TimeProvider time) : ControllerBase
{
    private const string TimeFormat = "HH:mm";

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    // ---- Consultas -------------------------------------------------------------------------------------------------

    /// <summary>Citas del médico con ese paciente que ya pueden tener consulta (hoy o antes, confirmadas y sin consulta).</summary>
    [HttpGet("appointments/consultable")]
    [Authorize(Roles = Roles.Medico)]
    public async Task<ActionResult<IReadOnlyList<ConsultableAppointmentDto>>> Consultable([FromQuery] Guid? patientId, CancellationToken ct)
    {
        if (patientId is null) return Validation("patientId", "Indica el paciente.");

        var userId = User.UserId();
        var endOfToday = clock.Today.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var appointments = await db.Appointments.AsNoTracking()
            .Where(a => a.PatientId == patientId && a.Doctor.UserId == userId && a.Status == AppointmentStatus.Confirmed
                        && a.StartsAtLocal < endOfToday
                        && !db.Consultations.Any(c => c.AppointmentId == a.Id))
            .OrderByDescending(a => a.StartsAtLocal)
            .ToListAsync(ct);

        return appointments.Select(a => new ConsultableAppointmentDto(a.Id, DateOnly.FromDateTime(a.StartsAtLocal), a.StartsAtLocal.ToString(TimeFormat, CultureInfo.InvariantCulture))).ToList();
    }

    /// <summary>Registra la consulta de una cita del propio médico. Una consulta por cita; solo citas de hoy o anteriores.</summary>
    [HttpPost("consultations")]
    [Authorize(Roles = Roles.Medico)]
    public async Task<ActionResult<ConsultationDto>> CreateConsultation(CreateConsultationRequest request, CancellationToken ct)
    {
        var userId = User.UserId();
        var appointment = await db.Appointments.Include(a => a.Patient).Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.Id == request.AppointmentId, ct);
        if (appointment is null) return NotFound();

        // El médico solo registra consultas de citas de su propia agenda.
        if (!(await authorization.AuthorizeAsync(User, appointment, AppointmentOperations.Read)).Succeeded)
            return await DeniedAsync(nameof(Appointment), appointment.Id, "create_consultation", ct);

        if (appointment.Status != AppointmentStatus.Confirmed)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Cita cancelada", detail: "No se puede registrar una consulta de una cita cancelada.");
        if (DateOnly.FromDateTime(appointment.StartsAtLocal) > clock.Today)
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Cita futura", detail: "Solo se puede registrar la consulta de citas de hoy o anteriores.");
        if (await db.Consultations.AnyAsync(c => c.AppointmentId == appointment.Id, ct))
            return AlreadyRegistered();

        var consultation = new Consultation
        {
            PatientId = appointment.PatientId,
            DoctorId = appointment.DoctorId,
            AppointmentId = appointment.Id,
            Reason = request.Reason.Trim(),
            Notes = request.Notes.Trim(),
            Diagnosis = request.Diagnosis.Trim(),
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        db.Consultations.Add(consultation);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Dos envíos simultáneos de la misma consulta: la base de datos deja pasar solo uno.
            return AlreadyRegistered();
        }

        await audit.WriteAsync(new AuditRecord(AuditActions.ConsultationCreated, AuditOutcome.Success, userId, nameof(Consultation), consultation.Id.ToString(), ClientIp), ct);
        consultation.Appointment = appointment;
        consultation.Doctor = appointment.Doctor;
        return CreatedAtAction(nameof(GetConsultation), new { id = consultation.Id }, ToDto(consultation, userId));
    }

    /// <summary>
    /// Consulta por identificador. Expediente compartido: la lee cualquier médico con relación de atención con el paciente
    /// (cita no cancelada o consulta registrada), no solo su autor. Ningún médico puede modificar la de otro.
    /// </summary>
    [HttpGet("consultations/{id:guid}")]
    [Authorize(Roles = Roles.Medico)]
    public async Task<ActionResult<ConsultationDto>> GetConsultation(Guid id, CancellationToken ct)
    {
        var userId = User.UserId();
        var consultation = await db.Consultations.AsNoTracking().Include(c => c.Appointment).Include(c => c.Doctor).Include(c => c.Patient)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (consultation is null) return NotFound();
        if (!(await authorization.AuthorizeAsync(User, consultation.Patient, PatientOperations.Read)).Succeeded)
            return await DeniedAsync(nameof(Consultation), id, "read_consultation", ct);

        var isOwn = consultation.Doctor.UserId == userId;
        await audit.WriteAsync(new AuditRecord(AuditActions.ConsultationRead, AuditOutcome.Success, userId, nameof(Consultation), id.ToString(), ClientIp,
            isOwn ? null : "other_author"), ct);
        return ToDto(consultation, userId);
    }

    /// <summary>
    /// Consultas de un paciente (expediente compartido). Requiere relación de atención con el paciente y devuelve las
    /// consultas de todos los médicos; <c>IsOwn</c> marca las del médico que consulta.
    /// </summary>
    [HttpGet("patients/{patientId:guid}/consultations")]
    [Authorize(Roles = Roles.Medico)]
    public async Task<ActionResult<IReadOnlyList<ConsultationDto>>> PatientConsultations(Guid patientId, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null) return NotFound();
        if (!(await authorization.AuthorizeAsync(User, patient, PatientOperations.Read)).Succeeded)
            return await DeniedAsync(nameof(Patient), patientId, "consultations", ct);

        var userId = User.UserId();
        var consultations = await db.Consultations.AsNoTracking().Include(c => c.Appointment).Include(c => c.Doctor)
            .Where(c => c.PatientId == patientId)
            .OrderByDescending(c => c.Appointment.StartsAtLocal)
            .ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.ConsultationsListed, AuditOutcome.Success, userId, nameof(Patient), patientId.ToString(), ClientIp,
            $"count={consultations.Count};others={consultations.Count(c => c.Doctor.UserId != userId)}"), ct);
        return consultations.Select(c => ToDto(c, userId)).ToList();
    }

    // ---- Recetas ---------------------------------------------------------------------------------------------------

    /// <summary>Crea una receta con uno o varios medicamentos para una consulta del propio médico.</summary>
    [HttpPost("prescriptions")]
    [Authorize(Roles = Roles.Medico)]
    public async Task<ActionResult<PrescriptionDto>> CreatePrescription(CreatePrescriptionRequest request, CancellationToken ct)
    {
        var userId = User.UserId();
        var consultation = await db.Consultations.Include(c => c.Patient).Include(c => c.Doctor).Include(c => c.Appointment)
            .FirstOrDefaultAsync(c => c.Id == request.ConsultationId, ct);
        if (consultation is null) return NotFound();
        if (consultation.Doctor.UserId != userId)
            return await DeniedAsync(nameof(Consultation), consultation.Id, "create_prescription", ct);

        var prescription = new Prescription
        {
            PatientId = consultation.PatientId,
            DoctorId = consultation.DoctorId,
            ConsultationId = consultation.Id,
            CreatedAtUtc = time.GetUtcNow().UtcDateTime,
            Items = request.Items.Select((item, index) => new PrescriptionItem
            {
                Position = index + 1,
                Medication = item.Medication.Trim(),
                Dose = item.Dose.Trim(),
                Frequency = item.Frequency.Trim(),
                Duration = item.Duration.Trim(),
                Instructions = item.Instructions.Trim(),
            }).ToList(),
        };
        db.Prescriptions.Add(prescription);
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.PrescriptionCreated, AuditOutcome.Success, userId, nameof(Prescription), prescription.Id.ToString(), ClientIp,
            $"items={prescription.Items.Count}"), ct);
        prescription.Patient = consultation.Patient;
        prescription.Doctor = consultation.Doctor;
        return CreatedAtAction(nameof(GetPrescription), new { id = prescription.Id }, ToDto(prescription, userId));
    }

    /// <summary>
    /// Receta por identificador: la lee su autor, cualquier médico con relación de atención con el paciente (expediente
    /// compartido) y el propio paciente. Nadie la modifica.
    /// </summary>
    [HttpGet("prescriptions/{id:guid}")]
    [Authorize(Roles = $"{Roles.Medico},{Roles.Paciente}")]
    public async Task<ActionResult<PrescriptionDto>> GetPrescription(Guid id, CancellationToken ct)
    {
        var prescription = await db.Prescriptions.AsNoTracking().Include(p => p.Items).Include(p => p.Patient).Include(p => p.Doctor)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (prescription is null) return NotFound();

        if (!(await authorization.AuthorizeAsync(User, prescription, PrescriptionOperations.Read)).Succeeded)
            return await DeniedAsync(nameof(Prescription), id, "read_prescription", ct);

        var userId = User.UserId();
        await audit.WriteAsync(new AuditRecord(AuditActions.PrescriptionRead, AuditOutcome.Success, userId, nameof(Prescription), id.ToString(), ClientIp,
            User.IsInRole(Roles.Medico) && prescription.Doctor.UserId != userId ? "other_author" : null), ct);
        return ToDto(prescription, userId);
    }

    /// <summary>Recetas del paciente autenticado (la app móvil).</summary>
    [HttpGet("prescriptions/me")]
    [Authorize(Roles = Roles.Paciente)]
    public async Task<ActionResult<IReadOnlyList<PrescriptionDto>>> MyPrescriptions(CancellationToken ct)
    {
        var userId = User.UserId();
        var prescriptions = await db.Prescriptions.AsNoTracking().Include(p => p.Items).Include(p => p.Patient).Include(p => p.Doctor)
            .Where(p => p.Patient.UserId == userId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .Take(100)
            .ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.PrescriptionsListed, AuditOutcome.Success, userId, nameof(Prescription), IpAddress: ClientIp,
            Details: $"own;count={prescriptions.Count}"), ct);
        return prescriptions.Select(p => ToDto(p, null)).ToList();
    }

    /// <summary>Recetas de un paciente de todos los médicos (expediente compartido; requiere relación de atención con el paciente).</summary>
    [HttpGet("patients/{patientId:guid}/prescriptions")]
    [Authorize(Roles = Roles.Medico)]
    public async Task<ActionResult<IReadOnlyList<PrescriptionDto>>> PatientPrescriptions(Guid patientId, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null) return NotFound();
        if (!(await authorization.AuthorizeAsync(User, patient, PatientOperations.Read)).Succeeded)
            return await DeniedAsync(nameof(Patient), patientId, "prescriptions", ct);

        var userId = User.UserId();
        var prescriptions = await db.Prescriptions.AsNoTracking().Include(p => p.Items).Include(p => p.Patient).Include(p => p.Doctor)
            .Where(p => p.PatientId == patientId)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.PrescriptionsListed, AuditOutcome.Success, userId, nameof(Patient), patientId.ToString(), ClientIp,
            $"count={prescriptions.Count};others={prescriptions.Count(p => p.Doctor.UserId != userId)}"), ct);
        return prescriptions.Select(p => ToDto(p, userId)).ToList();
    }

    // ---- Apoyo -----------------------------------------------------------------------------------------------------

    private ObjectResult AlreadyRegistered() => Problem(statusCode: StatusCodes.Status409Conflict, title: "Consulta ya registrada",
        detail: "Esta cita ya tiene una consulta registrada.");

    private async Task<ActionResult> DeniedAsync(string resourceType, Guid resourceId, string reason, CancellationToken ct)
    {
        await audit.WriteAsync(new AuditRecord(AuditActions.ClinicalAccessDenied, AuditOutcome.Failure, User.UserId(), resourceType, resourceId.ToString(), ClientIp, reason), ct);
        // 404 y no 403: no se confirma a quien no tiene acceso que ese recurso existe.
        return NotFound();
    }

    private ActionResult Validation(string field, string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));

    private static ConsultationDto ToDto(Consultation c, Guid? userId) => new(
        c.Id, c.PatientId, c.AppointmentId, DateOnly.FromDateTime(c.Appointment.StartsAtLocal), c.Doctor.FullName, c.Reason, c.Diagnosis, c.Notes,
        userId is not null && c.Doctor.UserId == userId);

    private PrescriptionDto ToDto(Prescription p, Guid? userId) => new(
        p.Id, p.PatientId, p.Patient.FullName, p.DoctorId, p.Doctor.FullName, p.ConsultationId,
        DateOnly.FromDateTime(clock.ToLocal(p.CreatedAtUtc)),
        p.Items.OrderBy(i => i.Position).Select(i => new PrescriptionItemDto(i.Medication, i.Dose, i.Frequency, i.Duration, i.Instructions)).ToList(),
        userId is not null && p.Doctor.UserId == userId);
}
