using MediCore.Api.Security;
using MediCore.Contracts;
using MediCore.Contracts.Auth;
using MediCore.Contracts.Users;
using MediCore.Domain.Auditing;
using MediCore.Domain.Identity;
using MediCore.Infrastructure.Auditing;
using MediCore.Infrastructure.Identity;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Controllers;

[ApiController]
[Route("api/users")]
[Authorize(Roles = Roles.Administrador)]
public class UsersController(MediCoreDbContext db, ISessionStore sessions, IAuditLogger audit) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync(ct);
        return users.Select(AuthController.ToDto).ToList();
    }

    [HttpPut("{id:guid}/role")]
    public async Task<IActionResult> ChangeRole(Guid id, ChangeRoleRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<UserRole>(request.Role, ignoreCase: false, out var role) || !Enum.IsDefined(role))
        {
            ModelState.AddModelError(nameof(request.Role), "El rol no es válido.");
            return ValidationProblem(ModelState);
        }

        if (id == User.UserId())
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "Operación no permitida", detail: "No puedes cambiar tu propio rol.");

        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return NotFound();

        if (user.Role == role)
            return NoContent();

        var previous = user.Role;
        user.Role = role;
        await db.SaveChangesAsync(ct);

        // Los tokens ya emitidos llevan el rol anterior: se cierran todas las sesiones del usuario.
        await sessions.RevokeAllForUserAsync(user.Id, ct);

        await audit.WriteAsync(new AuditRecord(AuditActions.UserRoleChanged, AuditOutcome.Success, User.UserId(), nameof(Domain.Identity.User), user.Id.ToString(),
            HttpContext.Connection.RemoteIpAddress?.ToString(), $"{previous}->{role}"), ct);
        return NoContent();
    }
}
