using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Contracts.Patients;
using MediCore.Domain.Auditing;
using MediCore.Domain.Patients;
using MediCore.Infrastructure.Auditing;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Controllers;

[ApiController]
[Route("api/patients")]
public class PatientsController(MediCoreDbContext db, IAuthorizationService authorization, IAuditLogger audit, TimeProvider clock) : ControllerBase
{
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>Búsqueda por nombre o identificación. El médico solo ve pacientes autorizados (hoy ninguno, ver <see cref="PatientAuthorizationHandler"/>).</summary>
    [HttpGet]
    [Authorize(Roles = $"{Roles.Recepcion},{Roles.Administrador},{Roles.Medico}")]
    public async Task<ActionResult<PagedResult<PatientDto>>> List([FromQuery] string? q, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        q = q?.Trim();
        if (q is { Length: > 100 })
            return ValidationProblem(new ValidationProblemDetails(new Dictionary<string, string[]> { ["q"] = ["La búsqueda es demasiado larga."] }));

        PagedResult<PatientDto> result;
        if (User.IsInRole(Roles.Medico))
        {
            result = new PagedResult<PatientDto>([], 0, page, pageSize);
        }
        else
        {
            var query = db.Patients.AsNoTracking();
            if (!string.IsNullOrEmpty(q))
            {
                var pattern = $"%{EscapeLike(q)}%";
                query = query.Where(p => EF.Functions.ILike(p.FullName, pattern) || EF.Functions.ILike(p.IdentificationNumber, pattern));
            }

            var total = await query.CountAsync(ct);
            var items = await query.OrderBy(p => p.FullName).ThenBy(p => p.Id)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(p => new PatientDto(p.Id, p.FullName, p.IdentificationNumber, p.BirthDate, p.Phone, p.Email))
                .ToListAsync(ct);
            result = new PagedResult<PatientDto>(items, total, page, pageSize);
        }

        // Se audita la consulta de datos de pacientes (sin registrar el texto buscado, que puede ser un nombre).
        await audit.WriteAsync(new AuditRecord(AuditActions.PatientSearched, AuditOutcome.Success, User.UserId(), nameof(Patient), IpAddress: ClientIp,
            Details: $"results={result.Items.Count}"), ct);
        return result;
    }

    /// <summary>Registro del paciente autenticado (la app móvil lo usa para conocer su identificador).</summary>
    [HttpGet("me")]
    [Authorize(Roles = Roles.Paciente)]
    public async Task<ActionResult<PatientDto>> Me(CancellationToken ct)
    {
        var userId = User.UserId();
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, ct);
        if (patient is null) return NotFound();

        await audit.WriteAsync(new AuditRecord(AuditActions.PatientRead, AuditOutcome.Success, userId, nameof(Patient), patient.Id.ToString(), ClientIp), ct);
        return ToDto(patient);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Recepcion},{Roles.Administrador},{Roles.Medico},{Roles.Paciente}")]
    public async Task<ActionResult<PatientDto>> GetById(Guid id, CancellationToken ct)
    {
        var patient = await db.Patients.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (patient is null) return NotFound();

        if (!(await authorization.AuthorizeAsync(User, patient, PatientOperations.Read)).Succeeded)
        {
            await audit.WriteAsync(new AuditRecord(AuditActions.PatientAccessDenied, AuditOutcome.Failure, User.UserId(), nameof(Patient), id.ToString(), ClientIp), ct);
            // 404 y no 403: no se confirma a quien no tiene acceso que ese paciente existe.
            return NotFound();
        }

        await audit.WriteAsync(new AuditRecord(AuditActions.PatientRead, AuditOutcome.Success, User.UserId(), nameof(Patient), id.ToString(), ClientIp), ct);
        return ToDto(patient);
    }

    [HttpPost]
    [Authorize(Roles = $"{Roles.Recepcion},{Roles.Administrador}")]
    public async Task<ActionResult<PatientDto>> Create(PatientRequest request, CancellationToken ct)
    {
        var patient = new Patient
        {
            FullName = request.FullName.Trim(),
            IdentificationNumber = request.IdentificationNumber.Trim(),
            NormalizedIdentification = Patient.NormalizeIdentification(request.IdentificationNumber),
            BirthDate = request.BirthDate!.Value,
            Phone = request.Phone.Trim(),
            Email = request.Email.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };

        db.Patients.Add(patient);
        if (await SaveOrConflictAsync(ct) is { } conflict) return conflict;

        await audit.WriteAsync(new AuditRecord(AuditActions.PatientCreated, AuditOutcome.Success, User.UserId(), nameof(Patient), patient.Id.ToString(), ClientIp), ct);
        return CreatedAtAction(nameof(GetById), new { id = patient.Id }, ToDto(patient));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = $"{Roles.Recepcion},{Roles.Administrador}")]
    public async Task<ActionResult<PatientDto>> Update(Guid id, PatientRequest request, CancellationToken ct)
    {
        var patient = await db.Patients.SingleOrDefaultAsync(p => p.Id == id, ct);
        if (patient is null) return NotFound();

        if (!(await authorization.AuthorizeAsync(User, patient, PatientOperations.Update)).Succeeded)
        {
            await audit.WriteAsync(new AuditRecord(AuditActions.PatientAccessDenied, AuditOutcome.Failure, User.UserId(), nameof(Patient), id.ToString(), ClientIp, "update"), ct);
            return NotFound();
        }

        patient.FullName = request.FullName.Trim();
        patient.IdentificationNumber = request.IdentificationNumber.Trim();
        patient.NormalizedIdentification = Patient.NormalizeIdentification(request.IdentificationNumber);
        patient.BirthDate = request.BirthDate!.Value;
        patient.Phone = request.Phone.Trim();
        patient.Email = request.Email.Trim();
        patient.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        if (await SaveOrConflictAsync(ct) is { } conflict) return conflict;

        await audit.WriteAsync(new AuditRecord(AuditActions.PatientUpdated, AuditOutcome.Success, User.UserId(), nameof(Patient), id.ToString(), ClientIp), ct);
        return ToDto(patient);
    }

    // La unicidad de la identificación se resuelve en la base de datos, también ante altas simultáneas.
    private async Task<ObjectResult?> SaveOrConflictAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Paciente duplicado", detail: "Ya existe un paciente con esa identificación.");
        }
    }

    private static string EscapeLike(string text) => text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    internal static PatientDto ToDto(Patient p) => new(p.Id, p.FullName, p.IdentificationNumber, p.BirthDate, p.Phone, p.Email);
}
