using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediCore.Contracts;
using MediCore.Contracts.Auth;
using MediCore.Contracts.Clinical;
using MediCore.Contracts.Doctors;
using MediCore.Contracts.Laboratory;
using MediCore.Contracts.Notifications;
using MediCore.Contracts.Scheduling;

namespace MediCore.Mobile.Core.Api;

/// <summary>
/// Cliente HTTP de la API real. Los tokens salen del <see cref="ISessionStore"/> (almacenamiento seguro en Android). El acceso dura 15 minutos y el
/// refresco es de un solo uso: se renueva antes de vencer o ante un 401, una sola vez a la vez (candado) y, si falla, se cierra la sesión local y se avisa.
/// </summary>
public sealed class MobileApiClient(HttpClient http, ISessionStore store, TimeProvider time, SessionEvents events) : IPatientApi
{
    private static readonly TimeSpan RefreshMargin = TimeSpan.FromSeconds(30);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    // ---- Sesión ----------------------------------------------------------------------------------------------------

    public async Task<ApiResult<string>> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        try
        {
            using var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(email, password), ct);
            if (!response.IsSuccessStatusCode)
                return new ApiResult<string>(response.StatusCode == HttpStatusCode.Unauthorized ? ApiStatus.InvalidCredentials : Map(response.StatusCode), default, await ReadMessageAsync(response, ct));

            var login = (await response.Content.ReadFromJsonAsync<LoginResponse>(ct))!;
            if (login.User.Role != Roles.Paciente)
            {
                // La app es solo para pacientes. El token emitido se descarta: no se guarda ni se usa.
                await SendLogoutAsync(login.AccessToken, ct);
                return new ApiResult<string>(ApiStatus.Forbidden, default, "Esta aplicación es solo para pacientes.");
            }

            await store.SaveAsync(new StoredSession(login.AccessToken, login.RefreshToken, Expiry(login.ExpiresInSeconds), login.User.DisplayName));
            return new ApiResult<string>(ApiStatus.Ok, login.User.DisplayName);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            return new ApiResult<string>(ApiStatus.Offline);
        }
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var session = await store.LoadAsync();
        await store.ClearAsync(); // la sesión local se elimina siempre, aunque la API no responda
        if (session is not null) await SendLogoutAsync(session.AccessToken, ct);
    }

    public async Task<bool> HasSessionAsync() => await store.LoadAsync() is not null;

    public async Task<string?> GetUserNameAsync() => (await store.LoadAsync())?.DisplayName;

    private async Task SendLogoutAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var _ = await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            // Sin conexión: la sesión del servidor caduca sola (8 horas) y la local ya se borró.
        }
    }

    // ---- Catálogo y agenda -----------------------------------------------------------------------------------------

    public async Task<ApiResult<IReadOnlyList<DoctorDto>>> GetDoctorsAsync(CancellationToken ct = default)
        => AsReadOnly(await CallAsync<List<DoctorDto>>(HttpMethod.Get, "api/doctors", null, null, ct));

    public Task<ApiResult<AvailabilityDto>> GetAvailabilityAsync(Guid doctorId, DateOnly date, CancellationToken ct = default)
        => CallAsync<AvailabilityDto>(HttpMethod.Get, $"api/doctors/{doctorId}/availability?date={date:yyyy-MM-dd}", null, null, ct);

    public async Task<ApiResult<IReadOnlyList<AppointmentDto>>> GetMyAppointmentsAsync(CancellationToken ct = default)
        => AsReadOnly(await CallAsync<List<AppointmentDto>>(HttpMethod.Get, "api/appointments/me", null, null, ct));

    public Task<ApiResult<AppointmentDto>> GetAppointmentAsync(Guid id, CancellationToken ct = default)
        => CallAsync<AppointmentDto>(HttpMethod.Get, $"api/appointments/{id}", null, null, ct);

    public Task<ApiResult<AppointmentDto>> CreateAppointmentAsync(CreateAppointmentRequest request, string idempotencyKey, CancellationToken ct = default)
        => CallAsync<AppointmentDto>(HttpMethod.Post, "api/appointments", request, idempotencyKey, ct);

    public Task<ApiResult> CancelAppointmentAsync(Guid id, CancellationToken ct = default)
        => CallAsync(HttpMethod.Post, $"api/appointments/{id}/cancel", null, null, ct);

    // ---- Resultados, recetas y notificaciones ----------------------------------------------------------------------

    public async Task<ApiResult<IReadOnlyList<LabResultSummaryDto>>> GetMyLabResultsAsync(CancellationToken ct = default)
        => AsReadOnly(await CallAsync<List<LabResultSummaryDto>>(HttpMethod.Get, "api/labs/results/me", null, null, ct));

    public Task<ApiResult<LabResultDto>> GetLabResultAsync(Guid id, CancellationToken ct = default)
        => CallAsync<LabResultDto>(HttpMethod.Get, $"api/labs/results/{id}", null, null, ct);

    public async Task<ApiResult<IReadOnlyList<PrescriptionDto>>> GetMyPrescriptionsAsync(CancellationToken ct = default)
        => AsReadOnly(await CallAsync<List<PrescriptionDto>>(HttpMethod.Get, "api/prescriptions/me", null, null, ct));

    public Task<ApiResult<PrescriptionDto>> GetPrescriptionAsync(Guid id, CancellationToken ct = default)
        => CallAsync<PrescriptionDto>(HttpMethod.Get, $"api/prescriptions/{id}", null, null, ct);

    public Task<ApiResult<PagedResult<NotificationDto>>> GetNotificationsAsync(int page = 1, int pageSize = 50, CancellationToken ct = default)
        => CallAsync<PagedResult<NotificationDto>>(HttpMethod.Get, $"api/notifications?page={page}&pageSize={pageSize}", null, null, ct);

    public Task<ApiResult<UnreadCountDto>> GetUnreadCountAsync(CancellationToken ct = default)
        => CallAsync<UnreadCountDto>(HttpMethod.Get, "api/notifications/unread-count", null, null, ct);

    public Task<ApiResult> MarkNotificationReadAsync(Guid id, CancellationToken ct = default)
        => CallAsync(HttpMethod.Post, $"api/notifications/{id}/read", null, null, ct);

    // ---- Llamadas autenticadas -------------------------------------------------------------------------------------

    private static ApiResult<IReadOnlyList<T>> AsReadOnly<T>(ApiResult<List<T>> result) => new(result.Status, result.Value, result.Message);

    private async Task<ApiResult> CallAsync(HttpMethod method, string url, object? body, string? idempotencyKey, CancellationToken ct)
    {
        var (response, failure) = await SendAuthenticatedAsync(method, url, body, idempotencyKey, ct);
        if (response is null) return new ApiResult(failure);
        using (response)
            return response.IsSuccessStatusCode ? new ApiResult(ApiStatus.Ok) : new ApiResult(Map(response.StatusCode), await ReadMessageAsync(response, ct));
    }

    private async Task<ApiResult<T>> CallAsync<T>(HttpMethod method, string url, object? body, string? idempotencyKey, CancellationToken ct)
    {
        var (response, failure) = await SendAuthenticatedAsync(method, url, body, idempotencyKey, ct);
        if (response is null) return new ApiResult<T>(failure);
        using (response)
        {
            if (!response.IsSuccessStatusCode)
                return new ApiResult<T>(Map(response.StatusCode), default, await ReadMessageAsync(response, ct));
            try
            {
                return new ApiResult<T>(ApiStatus.Ok, await response.Content.ReadFromJsonAsync<T>(ct));
            }
            catch (JsonException)
            {
                return new ApiResult<T>(ApiStatus.Unavailable);
            }
        }
    }

    private async Task<(HttpResponseMessage? Response, ApiStatus Failure)> SendAuthenticatedAsync(HttpMethod method, string url, object? body, string? idempotencyKey, CancellationToken ct)
    {
        try
        {
            var session = await store.LoadAsync();
            if (session is null) return (null, ApiStatus.SessionExpired);

            if (session.AccessExpiresAtUtc - time.GetUtcNow().UtcDateTime < RefreshMargin)
            {
                session = await RefreshAsync(session, ct);
                if (session is null) return (null, ApiStatus.SessionExpired);
            }

            var response = await SendAsync(method, url, body, idempotencyKey, session.AccessToken, ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized) return (response, ApiStatus.Ok);

            // El servidor ya no acepta el acceso (vencido o sesión revocada): un intento de renovación y se repite la llamada.
            response.Dispose();
            session = await RefreshAsync(session, ct);
            if (session is null) return (null, ApiStatus.SessionExpired);
            response = await SendAsync(method, url, body, idempotencyKey, session.AccessToken, ct);
            if (response.StatusCode != HttpStatusCode.Unauthorized) return (response, ApiStatus.Ok);

            response.Dispose();
            await EndSessionAsync();
            return (null, ApiStatus.SessionExpired);
        }
        catch (Exception ex) when (IsConnectionFailure(ex, ct))
        {
            return (null, ApiStatus.Offline);
        }
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, object? body, string? idempotencyKey, string accessToken, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body, body.GetType()) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return await http.SendAsync(request, ct);
    }

    /// <summary>Renueva los tokens. El refresco es de un solo uso, así que un solo hilo lo hace; los demás reutilizan el resultado.</summary>
    private async Task<StoredSession?> RefreshAsync(StoredSession used, CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var current = await store.LoadAsync();
            if (current is null) return null;
            if (current.RefreshToken != used.RefreshToken) return current; // otra llamada ya renovó

            using var response = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(current.RefreshToken), ct);
            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest or HttpStatusCode.Forbidden)
                    await EndSessionAsync();
                return null;
            }

            var refreshed = (await response.Content.ReadFromJsonAsync<RefreshResponse>(ct))!;
            var session = current with { AccessToken = refreshed.AccessToken, RefreshToken = refreshed.RefreshToken, AccessExpiresAtUtc = Expiry(refreshed.ExpiresInSeconds) };
            await store.SaveAsync(session);
            return session;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task EndSessionAsync()
    {
        await store.ClearAsync();
        events.RaiseSessionEnded();
    }

    private DateTime Expiry(int expiresInSeconds) => time.GetUtcNow().UtcDateTime.AddSeconds(expiresInSeconds);

    private static bool IsConnectionFailure(Exception ex, CancellationToken ct)
        => ex is HttpRequestException || (ex is TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested);

    private static ApiStatus Map(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Unauthorized => ApiStatus.SessionExpired,
        HttpStatusCode.Forbidden => ApiStatus.Forbidden,
        HttpStatusCode.NotFound => ApiStatus.NotFound,
        HttpStatusCode.Conflict => ApiStatus.Conflict,
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => ApiStatus.Invalid,
        HttpStatusCode.TooManyRequests => ApiStatus.RateLimited,
        _ => ApiStatus.Unavailable,
    };

    /// <summary>Mensaje en español de una respuesta ProblemDetails (el primer error de validación o el detalle).</summary>
    private static async Task<string?> ReadMessageAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            var root = document.RootElement;
            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
                foreach (var field in errors.EnumerateObject())
                    foreach (var message in field.Value.EnumerateArray())
                        return message.GetString();
            return root.TryGetProperty("detail", out var detail) ? detail.GetString() : null;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or HttpRequestException)
        {
            return null;
        }
    }
}

/// <summary>Avisos de la capa de sesión a la interfaz (por ejemplo, para volver al inicio de sesión cuando la sesión vence).</summary>
public sealed class SessionEvents
{
    public event Action? SessionEnded;

    public void RaiseSessionEnded() => SessionEnded?.Invoke();
}
