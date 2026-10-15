using MediCore.Domain.Auditing;
using MediCore.Infrastructure.Persistence;

namespace MediCore.Infrastructure.Auditing;

public record AuditRecord(
    string Action,
    string Outcome,
    Guid? ActorUserId = null,
    string? ResourceType = null,
    string? ResourceId = null,
    string? IpAddress = null,
    string? Details = null);

public interface IAuditLogger
{
    Task WriteAsync(AuditRecord record, CancellationToken ct = default);
}

internal sealed class AuditLogger(MediCoreDbContext db, TimeProvider clock) : IAuditLogger
{
    public async Task WriteAsync(AuditRecord record, CancellationToken ct = default)
    {
        db.AuditEntries.Add(new AuditEntry
        {
            OccurredAtUtc = clock.GetUtcNow().UtcDateTime,
            ActorUserId = record.ActorUserId,
            Action = record.Action,
            ResourceType = record.ResourceType,
            ResourceId = record.ResourceId,
            Outcome = record.Outcome,
            IpAddress = record.IpAddress,
            Details = record.Details,
        });
        await db.SaveChangesAsync(ct);
    }
}

/// <summary>Acciones auditadas por el módulo de identidad.</summary>
public static class AuditActions
{
    public const string LoginSucceeded = "auth.login";
    public const string LoginFailed = "auth.login_failed";
    public const string Logout = "auth.logout";
    public const string RefreshReuseDetected = "auth.refresh_reuse";
    public const string UserRoleChanged = "users.role_changed";
    public const string AuditViewed = "audit.viewed";
}
