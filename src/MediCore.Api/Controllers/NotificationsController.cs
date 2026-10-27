using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Contracts.Notifications;
using MediCore.Domain.Auditing;
using MediCore.Domain.Notifications;
using MediCore.Infrastructure.Auditing;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Controllers;

/// <summary>
/// Notificaciones internas del paciente autenticado (la consulta la futura app móvil, MOB-12). Cada paciente ve solo las suyas: la pertenencia se
/// resuelve por <c>Patient.UserId</c> y una notificación ajena responde 404.
/// </summary>
[ApiController]
[Route("api/notifications")]
[Authorize(Roles = Roles.Paciente)]
public class NotificationsController(MediCoreDbContext db, IAuditLogger audit, TimeProvider time) : ControllerBase
{
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    private IQueryable<Notification> Mine(Guid? userId) => db.Notifications.Where(n => n.Patient.UserId == userId);

    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationDto>>> List([FromQuery] bool unreadOnly = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var userId = User.UserId();

        var query = Mine(userId).AsNoTracking();
        if (unreadOnly) query = query.Where(n => n.ReadAtUtc == null);

        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(n => n.CreatedAtUtc).ThenBy(n => n.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.NotificationsListed, AuditOutcome.Success, userId, nameof(Notification), IpAddress: ClientIp,
            Details: $"count={rows.Count}"), ct);
        return new PagedResult<NotificationDto>(rows.Select(ToDto).ToList(), total, page, pageSize);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> UnreadCount(CancellationToken ct)
        => new UnreadCountDto(await Mine(User.UserId()).CountAsync(n => n.ReadAtUtc == null, ct));

    /// <summary>Marca la notificación como leída. Es idempotente: marcarla otra vez no cambia la fecha original.</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken ct)
    {
        var userId = User.UserId();
        var now = time.GetUtcNow().UtcDateTime;

        // UPDATE condicional: sin pérdida de actualizaciones y sin leer la fila para escribirla.
        var changed = await Mine(userId).Where(n => n.Id == id && n.ReadAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAtUtc, now), ct);
        if (changed == 0 && !await Mine(userId).AnyAsync(n => n.Id == id, ct))
            return NotFound();

        await audit.WriteAsync(new AuditRecord(AuditActions.NotificationRead, AuditOutcome.Success, userId, nameof(Notification), id.ToString(), ClientIp), ct);
        return NoContent();
    }

    private static NotificationDto ToDto(Notification n) => new(n.Id, n.Type, n.Title, n.Body, n.CreatedAtUtc, n.ReadAtUtc is not null, "LabResult", n.SourceId);
}
