using MediCore.Api.Security;
using MediCore.Contracts.Auth;
using MediCore.Domain.Auditing;
using MediCore.Domain.Identity;
using MediCore.Infrastructure.Auditing;
using MediCore.Infrastructure.Identity;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MediCore.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(
    MediCoreDbContext db,
    IPasswordHasher<User> hasher,
    ISessionStore sessions,
    JwtTokenService tokens,
    IAuditLogger audit,
    TimeProvider clock,
    IOptions<JwtOptions> jwt) : ControllerBase
{
    // Para que responder a un correo inexistente tarde lo mismo que a uno existente.
    private static readonly Lazy<(User User, string Hash)> Dummy = new(() =>
    {
        var user = new User { Email = "dummy@example.com", NormalizedEmail = "dummy@example.com", DisplayName = "dummy", PasswordHash = "" };
        return (user, new PasswordHasher<User>().HashPassword(user, "contrasena-descartable"));
    });

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var normalized = Domain.Identity.User.NormalizeEmail(request.Email);
        var user = await db.Users.SingleOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);

        if (user is null)
        {
            hasher.VerifyHashedPassword(Dummy.Value.User, Dummy.Value.Hash, request.Password);
            await audit.WriteAsync(new AuditRecord(AuditActions.LoginFailed, AuditOutcome.Failure, IpAddress: ClientIp, Details: "unknown_user"), ct);
            return InvalidCredentials();
        }

        if (!user.IsActive || user.IsLockedOut(now))
        {
            await audit.WriteAsync(new AuditRecord(AuditActions.LoginFailed, AuditOutcome.Failure, user.Id, nameof(User), user.Id.ToString(), ClientIp,
                user.IsActive ? "locked_out" : "inactive"), ct);
            return InvalidCredentials();
        }

        var verification = hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            user.RegisterFailedLogin(now);
            await db.SaveChangesAsync(ct);
            await audit.WriteAsync(new AuditRecord(AuditActions.LoginFailed, AuditOutcome.Failure, user.Id, nameof(User), user.Id.ToString(), ClientIp, "bad_password"), ct);
            return InvalidCredentials();
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = hasher.HashPassword(user, request.Password);
        user.RegisterSuccessfulLogin();
        await db.SaveChangesAsync(ct);

        var (sessionId, refreshToken) = await sessions.CreateAsync(user.Id, jwt.Value.SessionLifetime, ct);
        var access = tokens.Create(user, sessionId);
        await audit.WriteAsync(new AuditRecord(AuditActions.LoginSucceeded, AuditOutcome.Success, user.Id, nameof(User), user.Id.ToString(), ClientIp), ct);

        return new LoginResponse(access.Value, access.ExpiresInSeconds, refreshToken, ToDto(user));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting("auth")]
    public async Task<ActionResult<RefreshResponse>> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var result = await sessions.RotateAsync(request.RefreshToken, ct);

        if (result.Status == RefreshStatus.ReuseDetected)
        {
            await audit.WriteAsync(new AuditRecord(AuditActions.RefreshReuseDetected, AuditOutcome.Failure, result.UserId, nameof(User), result.UserId?.ToString(), ClientIp, "session_revoked"), ct);
            return InvalidSession();
        }

        if (result.Status != RefreshStatus.Rotated)
            return InvalidSession();

        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == result.UserId, ct);
        if (user is null || !user.IsActive)
        {
            await sessions.RevokeAsync(result.SessionId!, ct);
            return InvalidSession();
        }

        var access = tokens.Create(user, result.SessionId!);
        return new RefreshResponse(access.Value, access.ExpiresInSeconds, result.NewRefreshToken!);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await sessions.RevokeAsync(User.SessionId()!, ct);
        await audit.WriteAsync(new AuditRecord(AuditActions.Logout, AuditOutcome.Success, User.UserId(), nameof(Domain.Identity.User), User.UserId()?.ToString(), ClientIp), ct);
        return NoContent();
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserDto>> Me(CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == User.UserId(), ct);
        return user is null ? Unauthorized() : ToDto(user);
    }

    internal static UserDto ToDto(User user) => new(user.Id, user.DisplayName, user.Email, user.Role.ToString());

    // Mismo mensaje para todos los fallos: no revela si el correo existe, está bloqueado o está inactivo.
    private ObjectResult InvalidCredentials() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "No se pudo iniciar sesión",
        detail: "Credenciales inválidas o cuenta temporalmente no disponible.");

    private ObjectResult InvalidSession() => Problem(
        statusCode: StatusCodes.Status401Unauthorized,
        title: "Sesión no válida",
        detail: "Inicia sesión de nuevo.");
}
