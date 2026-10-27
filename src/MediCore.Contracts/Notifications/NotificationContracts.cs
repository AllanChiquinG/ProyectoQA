namespace MediCore.Contracts.Notifications;

/// <summary>Notificación interna del paciente (MOB-12). <c>TargetType</c>/<c>TargetId</c> indican a qué recurso lleva (por ejemplo, <c>LabResult</c>).</summary>
public record NotificationDto(Guid Id, string Type, string Title, string Body, DateTime CreatedAtUtc, bool IsRead, string TargetType, Guid TargetId);

public record UnreadCountDto(int Count);
