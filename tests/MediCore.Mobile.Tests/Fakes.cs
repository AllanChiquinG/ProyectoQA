using System.Net;
using System.Net.Http.Json;
using MediCore.Contracts;
using MediCore.Contracts.Clinical;
using MediCore.Contracts.Doctors;
using MediCore.Contracts.Laboratory;
using MediCore.Contracts.Notifications;
using MediCore.Contracts.Scheduling;
using MediCore.Mobile.Core.Api;
using MediCore.Mobile.Core.Services;

namespace MediCore.Mobile.Tests;

/// <summary>Reloj de la clínica fijo: 14/10/2026 07:00.</summary>
public sealed class FixedClock : IClinicClock
{
    public DateTime Now { get; set; } = new(2026, 10, 14, 7, 0, 0);
    public DateOnly Today => DateOnly.FromDateTime(Now);
    public DateOnly LastBookableDate => Today.AddDays(90);
}

public sealed class FakeNavigator : INavigator
{
    public List<string> Log { get; } = [];
    public string? LoginNotice { get; private set; }

    public Task GoToLoginAsync(string? notice = null) { Log.Add("login"); LoginNotice = notice; return Task.CompletedTask; }
    public Task GoToHomeAsync() { Log.Add("home"); return Task.CompletedTask; }
    public Task GoToAppointmentsAsync() { Log.Add("appointments"); return Task.CompletedTask; }
    public Task GoToAppointmentAsync(Guid id) { Log.Add($"appointment:{id}"); return Task.CompletedTask; }
    public Task GoToDoctorsAsync() { Log.Add("doctors"); return Task.CompletedTask; }
    public Task GoToSlotsAsync() { Log.Add("slots"); return Task.CompletedTask; }
    public Task GoToConfirmAsync() { Log.Add("confirm"); return Task.CompletedTask; }
    public Task GoToLabResultsAsync() { Log.Add("labs"); return Task.CompletedTask; }
    public Task GoToLabResultAsync(Guid id) { Log.Add($"lab:{id}"); return Task.CompletedTask; }
    public Task GoToPrescriptionsAsync() { Log.Add("prescriptions"); return Task.CompletedTask; }
    public Task GoToPrescriptionAsync(Guid id) { Log.Add($"prescription:{id}"); return Task.CompletedTask; }
    public Task GoToNotificationsAsync() { Log.Add("notifications"); return Task.CompletedTask; }
    public Task GoBackAsync() { Log.Add("back"); return Task.CompletedTask; }
}

public sealed class FakeDialogs : IDialogs
{
    public bool Answer { get; set; } = true;
    public List<string> Toasts { get; } = [];
    public int Confirms { get; private set; }

    public Task<bool> ConfirmAsync(string title, string message, string accept, string cancel) { Confirms++; return Task.FromResult(Answer); }
    public Task ToastAsync(string message) { Toasts.Add(message); return Task.CompletedTask; }
}

/// <summary>
/// API simulada SOLO para probar la lógica de los ViewModels de forma aislada (estados, reintentos, claves de idempotencia). Las pruebas contra la
/// API real están en <c>MobileApiIntegrationTests</c> (proyecto MediCore.Api.Tests).
/// </summary>
public sealed class FakeApi : IPatientApi
{
    public Func<string, string, ApiResult<string>> Login { get; set; } = (_, _) => new(ApiStatus.Ok, "Ana Sofía García");
    public Func<IReadOnlyList<DoctorDto>> Doctors { get; set; } = () => [];
    public Func<Guid, DateOnly, ApiResult<AvailabilityDto>> Availability { get; set; } = (d, date) => new(ApiStatus.Ok, new AvailabilityDto(d, date, true, [new SlotDto("08:00", true), new SlotDto("08:30", true)]));
    public Func<ApiResult<IReadOnlyList<AppointmentDto>>> MyAppointments { get; set; } = () => new(ApiStatus.Ok, []);
    public Func<Guid, ApiResult<AppointmentDto>> Appointment { get; set; } = id => new(ApiStatus.NotFound);
    public Func<CreateAppointmentRequest, string, ApiResult<AppointmentDto>> Create { get; set; } = (_, _) => new(ApiStatus.Unavailable);
    public Func<Guid, ApiResult> Cancel { get; set; } = _ => new(ApiStatus.Ok);
    public Func<ApiResult<PagedResult<NotificationDto>>> Notifications { get; set; } = () => new(ApiStatus.Ok, new PagedResult<NotificationDto>([], 0, 1, 50));
    public Func<Guid, ApiResult> MarkRead { get; set; } = _ => new(ApiStatus.Ok);
    public Func<ApiResult<IReadOnlyList<LabResultSummaryDto>>> Labs { get; set; } = () => new(ApiStatus.Ok, []);
    public Func<ApiResult<IReadOnlyList<PrescriptionDto>>> Prescriptions { get; set; } = () => new(ApiStatus.Ok, []);

    public List<string> Keys { get; } = [];
    public int CreateCalls { get; private set; }
    public int LogoutCalls { get; private set; }
    public TaskCompletionSource? CreateGate { get; set; }

    public Task<ApiResult<string>> LoginAsync(string email, string password, CancellationToken ct = default) => Task.FromResult(Login(email, password));
    public Task LogoutAsync(CancellationToken ct = default) { LogoutCalls++; return Task.CompletedTask; }
    public Task<bool> HasSessionAsync() => Task.FromResult(true);
    public Task<string?> GetUserNameAsync() => Task.FromResult<string?>("Ana Sofía García");
    public Task<ApiResult<IReadOnlyList<DoctorDto>>> GetDoctorsAsync(CancellationToken ct = default) => Task.FromResult(new ApiResult<IReadOnlyList<DoctorDto>>(ApiStatus.Ok, Doctors()));
    public Task<ApiResult<AvailabilityDto>> GetAvailabilityAsync(Guid doctorId, DateOnly date, CancellationToken ct = default) => Task.FromResult(Availability(doctorId, date));
    public Task<ApiResult<IReadOnlyList<AppointmentDto>>> GetMyAppointmentsAsync(CancellationToken ct = default) => Task.FromResult(MyAppointments());
    public Task<ApiResult<AppointmentDto>> GetAppointmentAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Appointment(id));

    public async Task<ApiResult<AppointmentDto>> CreateAppointmentAsync(CreateAppointmentRequest request, string idempotencyKey, CancellationToken ct = default)
    {
        CreateCalls++;
        Keys.Add(idempotencyKey);
        if (CreateGate is not null) await CreateGate.Task;
        return Create(request, idempotencyKey);
    }

    public Task<ApiResult> CancelAppointmentAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Cancel(id));
    public Task<ApiResult<IReadOnlyList<LabResultSummaryDto>>> GetMyLabResultsAsync(CancellationToken ct = default) => Task.FromResult(Labs());
    public Task<ApiResult<LabResultDto>> GetLabResultAsync(Guid id, CancellationToken ct = default) => Task.FromResult(new ApiResult<LabResultDto>(ApiStatus.NotFound));
    public Task<ApiResult<IReadOnlyList<PrescriptionDto>>> GetMyPrescriptionsAsync(CancellationToken ct = default) => Task.FromResult(Prescriptions());
    public Task<ApiResult<PrescriptionDto>> GetPrescriptionAsync(Guid id, CancellationToken ct = default) => Task.FromResult(new ApiResult<PrescriptionDto>(ApiStatus.NotFound));
    public Task<ApiResult<PagedResult<NotificationDto>>> GetNotificationsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default) => Task.FromResult(Notifications());
    public Task<ApiResult<UnreadCountDto>> GetUnreadCountAsync(CancellationToken ct = default) => Task.FromResult(new ApiResult<UnreadCountDto>(ApiStatus.Ok, new UnreadCountDto(2)));
    public Task<ApiResult> MarkNotificationReadAsync(Guid id, CancellationToken ct = default) => Task.FromResult(MarkRead(id));
}

/// <summary>Manejador HTTP que responde con lo que indique la prueba y registra las peticiones (para probar el cliente sin red).</summary>
public sealed class ScriptedHandler(Func<HttpRequestMessage, int, HttpResponseMessage> respond) : HttpMessageHandler
{
    private int _calls;
    public List<(string Method, string Path, string? Authorization, string? Key)> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        lock (Requests)
            Requests.Add((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues("Idempotency-Key", out var keys) ? keys.First() : null));
        return Task.FromResult(respond(request, Interlocked.Increment(ref _calls)));
    }

    public static HttpResponseMessage Json<T>(HttpStatusCode status, T body) => new(status) { Content = JsonContent.Create(body) };
}

public sealed class FixedTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
