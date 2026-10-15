namespace MediCore.Domain.Auditing;

/// <summary>Registro de auditoría. Solo se agregan; nunca se modifican ni se eliminan desde la aplicación.</summary>
public class AuditEntry
{
    public long Id { get; set; }
    public DateTime OccurredAtUtc { get; set; }

    /// <summary>Quien actúa. Nulo si no se pudo identificar (por ejemplo, un inicio de sesión fallido).</summary>
    public Guid? ActorUserId { get; set; }

    public required string Action { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public required string Outcome { get; set; }
    public string? IpAddress { get; set; }

    /// <summary>Código breve del motivo. No debe contener datos personales ni clínicos.</summary>
    public string? Details { get; set; }
}

public static class AuditOutcome
{
    public const string Success = "Success";
    public const string Failure = "Failure";
}
