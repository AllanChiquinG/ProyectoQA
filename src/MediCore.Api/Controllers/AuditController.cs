using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Contracts.Audit;
using MediCore.Domain.Auditing;
using MediCore.Infrastructure.Auditing;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize(Roles = Roles.Administrador)]
public class AuditController(MediCoreDbContext db, IAuditLogger audit) : ControllerBase
{
    /// <summary>Últimos registros de auditoría (más recientes primero). La propia consulta queda auditada.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<AuditEntryDto>>> List([FromQuery] int take = 100, CancellationToken ct = default)
    {
        take = Math.Clamp(take, 1, 500);
        var entries = await db.AuditEntries.AsNoTracking()
            .OrderByDescending(e => e.Id)
            .Take(take)
            .Select(e => new AuditEntryDto(e.Id, e.OccurredAtUtc, e.ActorUserId, e.Action, e.ResourceType, e.ResourceId, e.Outcome, e.IpAddress, e.Details))
            .ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.AuditViewed, AuditOutcome.Success, User.UserId(),
            IpAddress: HttpContext.Connection.RemoteIpAddress?.ToString()), ct);
        return entries;
    }
}
