namespace MediCore.Contracts.Audit;

public record AuditEntryDto(
    long Id,
    DateTime OccurredAtUtc,
    Guid? ActorUserId,
    string Action,
    string? ResourceType,
    string? ResourceId,
    string Outcome,
    string? IpAddress,
    string? Details);
