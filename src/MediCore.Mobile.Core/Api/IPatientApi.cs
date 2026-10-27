using MediCore.Contracts;
using MediCore.Contracts.Doctors;
using MediCore.Contracts.Laboratory;
using MediCore.Contracts.Notifications;
using MediCore.Contracts.Clinical;
using MediCore.Contracts.Scheduling;

namespace MediCore.Mobile.Core.Api;

/// <summary>Las llamadas de la app a la API real de MediCore. La autorización la valida siempre la API; la app solo la refleja en pantalla.</summary>
public interface IPatientApi
{
    Task<ApiResult<string>> LoginAsync(string email, string password, CancellationToken ct = default);
    Task LogoutAsync(CancellationToken ct = default);
    Task<bool> HasSessionAsync();

    /// <summary>Nombre del usuario con sesión iniciada (para el saludo), o nulo si no hay sesión.</summary>
    Task<string?> GetUserNameAsync();

    Task<ApiResult<IReadOnlyList<DoctorDto>>> GetDoctorsAsync(CancellationToken ct = default);
    Task<ApiResult<AvailabilityDto>> GetAvailabilityAsync(Guid doctorId, DateOnly date, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<AppointmentDto>>> GetMyAppointmentsAsync(CancellationToken ct = default);
    Task<ApiResult<AppointmentDto>> GetAppointmentAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<AppointmentDto>> CreateAppointmentAsync(CreateAppointmentRequest request, string idempotencyKey, CancellationToken ct = default);
    Task<ApiResult> CancelAppointmentAsync(Guid id, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<LabResultSummaryDto>>> GetMyLabResultsAsync(CancellationToken ct = default);
    Task<ApiResult<LabResultDto>> GetLabResultAsync(Guid id, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<PrescriptionDto>>> GetMyPrescriptionsAsync(CancellationToken ct = default);
    Task<ApiResult<PrescriptionDto>> GetPrescriptionAsync(Guid id, CancellationToken ct = default);

    Task<ApiResult<PagedResult<NotificationDto>>> GetNotificationsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<ApiResult<UnreadCountDto>> GetUnreadCountAsync(CancellationToken ct = default);
    Task<ApiResult> MarkNotificationReadAsync(Guid id, CancellationToken ct = default);
}
