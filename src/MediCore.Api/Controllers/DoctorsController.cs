using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Contracts.Doctors;
using MediCore.Domain.Auditing;
using MediCore.Domain.Doctors;
using MediCore.Infrastructure.Auditing;
using MediCore.Infrastructure.Caching;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Controllers;

[ApiController]
[Route("api/doctors")]
public class DoctorsController(MediCoreDbContext db, ICacheService cache, IAuditLogger audit, IConfiguration configuration, TimeProvider clock) : ControllerBase
{
    private TimeSpan CatalogTtl => configuration.GetValue("Cache:DoctorCatalogTtl", TimeSpan.FromMinutes(5));
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Catálogo de médicos (cacheado en Redis). Cualquier usuario autenticado puede consultarlo; el paciente solo ve
    /// nombre y especialidad, no el teléfono ni el correo.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<DoctorDto>>> List([FromQuery] string? q, CancellationToken ct)
    {
        var all = await cache.GetOrCreateAsync(CacheKeys.DoctorCatalog, CatalogTtl, async token =>
            await db.Doctors.AsNoTracking().OrderBy(d => d.FullName)
                .Select(d => new DoctorDto(d.Id, d.FullName, d.Specialty, d.Phone, d.Email)).ToListAsync(token), ct);

        IEnumerable<DoctorDto> result = all;
        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();
            result = result.Where(d => d.FullName.Contains(term, StringComparison.OrdinalIgnoreCase) || d.Specialty.Contains(term, StringComparison.OrdinalIgnoreCase));
        }

        if (User.IsInRole(Roles.Paciente))
            result = result.Select(d => d with { Phone = null, Email = null });

        return result.ToList();
    }

    [HttpGet("{id:guid}")]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<ActionResult<DoctorDto>> GetById(Guid id, CancellationToken ct)
    {
        var doctor = await db.Doctors.AsNoTracking().SingleOrDefaultAsync(d => d.Id == id, ct);
        return doctor is null ? NotFound() : ToDto(doctor);
    }

    [HttpPost]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<ActionResult<DoctorDto>> Create(DoctorRequest request, CancellationToken ct)
    {
        var doctor = new Doctor
        {
            FullName = request.FullName.Trim(),
            Specialty = request.Specialty.Trim(),
            Phone = request.Phone.Trim(),
            Email = request.Email.Trim(),
            CreatedAtUtc = clock.GetUtcNow().UtcDateTime,
        };
        db.Doctors.Add(doctor);
        await db.SaveChangesAsync(ct);

        await cache.RemoveAsync(CacheKeys.DoctorCatalog, ct);
        await audit.WriteAsync(new AuditRecord(AuditActions.DoctorCreated, AuditOutcome.Success, User.UserId(), nameof(Doctor), doctor.Id.ToString(), ClientIp), ct);
        return CreatedAtAction(nameof(GetById), new { id = doctor.Id }, ToDto(doctor));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = Roles.Administrador)]
    public async Task<ActionResult<DoctorDto>> Update(Guid id, DoctorRequest request, CancellationToken ct)
    {
        var doctor = await db.Doctors.SingleOrDefaultAsync(d => d.Id == id, ct);
        if (doctor is null) return NotFound();

        doctor.FullName = request.FullName.Trim();
        doctor.Specialty = request.Specialty.Trim();
        doctor.Phone = request.Phone.Trim();
        doctor.Email = request.Email.Trim();
        doctor.UpdatedAtUtc = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);

        // El catálogo cacheado ya no es válido: se invalida para que la próxima lectura vea el cambio.
        await cache.RemoveAsync(CacheKeys.DoctorCatalog, ct);
        await audit.WriteAsync(new AuditRecord(AuditActions.DoctorUpdated, AuditOutcome.Success, User.UserId(), nameof(Doctor), id.ToString(), ClientIp), ct);
        return ToDto(doctor);
    }

    private static DoctorDto ToDto(Doctor d) => new(d.Id, d.FullName, d.Specialty, d.Phone, d.Email);
}
