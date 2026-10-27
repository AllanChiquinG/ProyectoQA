using System.Text.Json;
using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Contracts.Laboratory;
using MediCore.Domain.Auditing;
using MediCore.Domain.Laboratory;
using MediCore.Domain.Messaging;
using MediCore.Domain.Notifications;
using MediCore.Domain.Patients;
using MediCore.Infrastructure.Auditing;
using MediCore.Infrastructure.Messaging;
using MediCore.Infrastructure.Persistence;
using MediCore.Infrastructure.Scheduling;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Controllers;

/// <summary>
/// Resultados de laboratorio. Laboratorio los registra y consulta; el Médico con relación de atención los lee desde el expediente; el Paciente solo los
/// suyos. Al registrar un resultado se guardan, en la misma transacción de PostgreSQL, el resultado y el evento <c>LabResultReady</c> pendiente
/// (outbox transaccional): si RabbitMQ no está disponible el resultado queda guardado y el evento se publica cuando el broker vuelve.
/// La auditoría registra quién accedió a qué resultado y cuándo, nunca su contenido (tipo de examen, valores ni observaciones).
/// </summary>
[ApiController]
[Route("api")]
public class LabsController(
    MediCoreDbContext db,
    IAuthorizationService authorization,
    IAuditLogger audit,
    IClinicClock clock,
    TimeProvider time) : ControllerBase
{
    private static readonly DateOnly EarliestExamDate = new(2000, 1, 1);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IQueryable<LabResultSummaryDto> Summaries(IQueryable<LabResult> query) => query.Select(r => new LabResultSummaryDto(
        r.Id, r.PatientId, r.Patient.FullName, r.ExamType, r.PerformedOn,
        db.Notifications.Any(n => n.Type == NotificationTypes.LabResultReady && n.SourceId == r.Id) ? NotificationStatuses.Sent : NotificationStatuses.Pending));

    // ---- Laboratorio -----------------------------------------------------------------------------------------------

    /// <summary>Pacientes que Laboratorio puede elegir al registrar un resultado: solo nombre e identificación.</summary>
    [HttpGet("labs/patients")]
    [Authorize(Roles = Roles.Laboratorio)]
    public async Task<ActionResult<IReadOnlyList<LabPatientDto>>> Patients([FromQuery] string? q, CancellationToken ct)
    {
        q = q?.Trim();
        if (q is { Length: > 100 }) return Validation("q", "La búsqueda es demasiado larga.");

        var query = db.Patients.AsNoTracking();
        if (!string.IsNullOrEmpty(q))
        {
            var pattern = $"%{EscapeLike(q)}%";
            query = query.Where(p => EF.Functions.ILike(p.FullName, pattern) || EF.Functions.ILike(p.IdentificationNumber, pattern));
        }

        var patients = await query.OrderBy(p => p.FullName).ThenBy(p => p.Id).Take(50)
            .Select(p => new LabPatientDto(p.Id, p.FullName, p.IdentificationNumber))
            .ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.LabPatientsSearched, AuditOutcome.Success, User.UserId(), nameof(Patient), IpAddress: ClientIp,
            Details: $"results={patients.Count}"), ct);
        return patients;
    }

    /// <summary>Registra un resultado. Guarda resultado y evento pendiente en una sola transacción.</summary>
    [HttpPost("labs/results")]
    [Authorize(Roles = Roles.Laboratorio)]
    public async Task<ActionResult<LabResultDto>> Create(CreateLabResultRequest request, CancellationToken ct)
    {
        var performedOn = request.PerformedOn!.Value;
        if (performedOn > clock.Today) return Validation(nameof(request.PerformedOn), "La fecha del examen no puede ser futura.");
        if (performedOn < EarliestExamDate) return Validation(nameof(request.PerformedOn), "La fecha del examen no es válida.");

        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == request.PatientId, ct);
        if (patient is null) return Validation(nameof(request.PatientId), "El paciente indicado no existe.");

        var userId = User.UserId()!.Value;
        var now = time.GetUtcNow().UtcDateTime;
        var result = new LabResult
        {
            PatientId = patient.Id,
            RegisteredByUserId = userId,
            ExamType = request.ExamType.Trim(),
            PerformedOn = performedOn,
            Notes = request.Notes?.Trim() ?? "",
            CreatedAtUtc = now,
            Parameters = request.Parameters.Select((p, index) => new LabResultParameter
            {
                Position = index + 1,
                Name = p.Name.Trim(),
                Value = p.Value.Trim(),
                Unit = p.Unit?.Trim() ?? "",
                ReferenceRange = p.ReferenceRange?.Trim() ?? "",
            }).ToList(),
        };

        db.LabResults.Add(result);
        // Outbox transaccional: el evento se agrega al mismo contexto, así que SaveChanges guarda ambos o ninguno.
        // El evento lleva solo identificadores.
        db.OutboxMessages.Add(NewReadyEvent(result, now));
        await db.SaveChangesAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.LabResultCreated, AuditOutcome.Success, userId, nameof(LabResult), result.Id.ToString(), ClientIp,
            $"parameters={result.Parameters.Count}"), ct);

        result.Patient = patient;
        return CreatedAtAction(nameof(Get), new { id = result.Id }, ToDto(result, NotificationStatuses.Pending));
    }

    /// <summary>Listado de resultados con el estado de la notificación (WEB-16). Solo Laboratorio.</summary>
    [HttpGet("labs/results")]
    [Authorize(Roles = Roles.Laboratorio)]
    public async Task<ActionResult<PagedResult<LabResultSummaryDto>>> List([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        q = q?.Trim();
        if (q is { Length: > 100 }) return Validation("q", "La búsqueda es demasiado larga.");

        var query = db.LabResults.AsNoTracking();
        if (!string.IsNullOrEmpty(q))
        {
            var pattern = $"%{EscapeLike(q)}%";
            query = query.Where(r => EF.Functions.ILike(r.Patient.FullName, pattern) || EF.Functions.ILike(r.ExamType, pattern));
        }

        var total = await query.CountAsync(ct);
        var items = await Summaries(query.OrderByDescending(r => r.PerformedOn).ThenByDescending(r => r.CreatedAtUtc).ThenBy(r => r.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.LabResultsListed, AuditOutcome.Success, User.UserId(), nameof(LabResult), IpAddress: ClientIp,
            Details: $"results={items.Count}"), ct);
        return new PagedResult<LabResultSummaryDto>(items, total, page, pageSize);
    }

    /// <summary>Resultados del paciente autenticado (la app móvil, MOB-08).</summary>
    [HttpGet("labs/results/me")]
    [Authorize(Roles = Roles.Paciente)]
    public async Task<ActionResult<IReadOnlyList<LabResultSummaryDto>>> Mine(CancellationToken ct)
    {
        var userId = User.UserId();
        var items = await Summaries(db.LabResults.AsNoTracking().Where(r => r.Patient.UserId == userId)
            .OrderByDescending(r => r.PerformedOn).ThenByDescending(r => r.CreatedAtUtc).Take(100)).ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.LabResultsListed, AuditOutcome.Success, userId, nameof(LabResult), IpAddress: ClientIp,
            Details: $"own;count={items.Count}"), ct);
        return items;
    }

    /// <summary>Detalle de un resultado (WEB-18, MOB-09). Lo leen Laboratorio, el médico con relación de atención y el paciente dueño.</summary>
    [HttpGet("labs/results/{id:guid}")]
    [Authorize(Roles = $"{Roles.Laboratorio},{Roles.Medico},{Roles.Paciente}")]
    public async Task<ActionResult<LabResultDto>> Get(Guid id, CancellationToken ct)
    {
        var result = await db.LabResults.AsNoTracking().Include(r => r.Parameters).Include(r => r.Patient).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (result is null) return NotFound();

        if (!(await authorization.AuthorizeAsync(User, result, LabResultOperations.Read)).Succeeded)
            return await DeniedAsync(nameof(LabResult), id, "read_lab_result", ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.LabResultRead, AuditOutcome.Success, User.UserId(), nameof(LabResult), id.ToString(), ClientIp), ct);
        return ToDto(result, await StatusAsync(id, ct));
    }

    /// <summary>Resultados de un paciente para el expediente (requiere relación válida de atención).</summary>
    [HttpGet("patients/{patientId:guid}/lab-results")]
    [Authorize(Roles = Roles.Medico)]
    public async Task<ActionResult<IReadOnlyList<LabResultSummaryDto>>> ForPatient(Guid patientId, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().FirstOrDefaultAsync(p => p.Id == patientId, ct);
        if (patient is null) return NotFound();
        if (!(await authorization.AuthorizeAsync(User, patient, PatientOperations.Read)).Succeeded)
            return await DeniedAsync(nameof(Patient), patientId, "lab_results", ct);

        var items = await Summaries(db.LabResults.AsNoTracking().Where(r => r.PatientId == patientId)
            .OrderByDescending(r => r.PerformedOn).ThenByDescending(r => r.CreatedAtUtc)).ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.LabResultsListed, AuditOutcome.Success, User.UserId(), nameof(Patient), patientId.ToString(), ClientIp,
            $"count={items.Count}"), ct);
        return items;
    }

    /// <summary>
    /// Reintentar la notificación (WEB-18). Si ya existe, no hace nada. Si el evento sigue pendiente de publicar, se adelanta su próximo intento; si ya
    /// se publicó y la notificación no llegó a generarse (por ejemplo, terminó en la cola de mensajes fallidos), se encola un evento nuevo. En todos los
    /// casos el consumidor es idempotente: no puede haber dos notificaciones por resultado.
    /// </summary>
    [HttpPost("labs/results/{id:guid}/notification/retry")]
    [Authorize(Roles = Roles.Laboratorio)]
    public async Task<ActionResult<RetryNotificationResponse>> RetryNotification(Guid id, CancellationToken ct)
    {
        var result = await db.LabResults.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
        if (result is null) return NotFound();

        if (await StatusAsync(id, ct) == NotificationStatuses.Sent)
            return new RetryNotificationResponse("AlreadySent");

        var now = time.GetUtcNow().UtcDateTime;
        var rescheduled = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE outbox_messages SET "NextAttemptAtUtc" = {now}
            WHERE "ProcessedAtUtc" IS NULL AND "Type" = {EventTypes.LabResultReady} AND "Payload"->>'labResultId' = {id.ToString()}
            """, ct);
        if (rescheduled == 0)
        {
            db.OutboxMessages.Add(NewReadyEvent(result, now));
            await db.SaveChangesAsync(ct);
        }

        await audit.WriteAsync(new AuditRecord(AuditActions.LabNotificationRetried, AuditOutcome.Success, User.UserId(), nameof(LabResult), id.ToString(), ClientIp,
            rescheduled > 0 ? "rescheduled" : "requeued"), ct);
        return Accepted(new RetryNotificationResponse("Queued"));
    }

    // ---- Apoyo -----------------------------------------------------------------------------------------------------

    private static OutboxMessage NewReadyEvent(LabResult result, DateTime now) => new()
    {
        Type = EventTypes.LabResultReady,
        Payload = JsonSerializer.Serialize(new LabResultReadyEvent(result.Id, result.PatientId), Json),
        OccurredAtUtc = now,
        NextAttemptAtUtc = now,
    };

    private async Task<string> StatusAsync(Guid labResultId, CancellationToken ct)
        => await db.Notifications.AnyAsync(n => n.Type == NotificationTypes.LabResultReady && n.SourceId == labResultId, ct)
            ? NotificationStatuses.Sent
            : NotificationStatuses.Pending;

    private async Task<ActionResult> DeniedAsync(string resourceType, Guid resourceId, string reason, CancellationToken ct)
    {
        await audit.WriteAsync(new AuditRecord(AuditActions.ClinicalAccessDenied, AuditOutcome.Failure, User.UserId(), resourceType, resourceId.ToString(), ClientIp, reason), ct);
        // 404 y no 403: no se confirma a quien no tiene acceso que ese recurso existe.
        return NotFound();
    }

    private ActionResult Validation(string field, string message)
        => ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { [field] = [message] }));

    private static string EscapeLike(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private LabResultDto ToDto(LabResult r, string status) => new(
        r.Id, r.PatientId, r.Patient.FullName, r.ExamType, r.PerformedOn, r.Notes,
        r.Parameters.OrderBy(p => p.Position).Select(p => new LabResultParameterDto(p.Name, p.Value, p.Unit, p.ReferenceRange)).ToList(),
        status);
}
