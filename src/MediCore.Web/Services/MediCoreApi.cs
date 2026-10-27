using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MediCore.Contracts;
using MediCore.Contracts.Auth;
using MediCore.Contracts.Doctors;
using MediCore.Contracts.Patients;
using MediCore.Contracts.Scheduling;
using MediCore.Contracts.Users;
using Microsoft.AspNetCore.Authentication;

namespace MediCore.Web.Services;

public enum ApiStatus { Ok, InvalidCredentials, Unauthorized, Forbidden, NotFound, Conflict, BadRequest, RateLimited, Unavailable }

/// <summary>Resultado de una llamada a la API. <see cref="Message"/> trae el mensaje en español de la API cuando hubo un error de negocio.</summary>
public record ApiResult(ApiStatus Status, string? Message = null)
{
    public bool IsOk => Status == ApiStatus.Ok;
}

public record ApiResult<T>(ApiStatus Status, T? Value = default, string? Message = null) : ApiResult(Status, Message);

/// <summary>Cliente de la API de MediCore. Las llamadas autenticadas usan el token guardado en la cookie del usuario.</summary>
public interface IMediCoreApi
{
    Task<ApiResult<LoginResponse>> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<RefreshResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string accessToken, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<UserDto>>> GetUsersAsync(CancellationToken ct = default);
    Task<ApiResult> ChangeRoleAsync(Guid userId, string role, CancellationToken ct = default);

    Task<ApiResult<PagedResult<PatientDto>>> GetPatientsAsync(string? query, int page, int pageSize = 20, CancellationToken ct = default);
    Task<ApiResult<PatientDto>> GetPatientAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<PatientDto>> SavePatientAsync(Guid? id, PatientRequest request, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<DoctorDto>>> GetDoctorsAsync(string? query, CancellationToken ct = default);
    Task<ApiResult<DoctorDto>> GetDoctorAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<DoctorDto>> GetMyDoctorAsync(CancellationToken ct = default);
    Task<ApiResult<DoctorDto>> SaveDoctorAsync(Guid? id, DoctorRequest request, CancellationToken ct = default);

    Task<ApiResult<ScheduleDto>> GetScheduleAsync(Guid doctorId, CancellationToken ct = default);
    Task<ApiResult<ScheduleDto>> SaveScheduleAsync(Guid doctorId, ScheduleRequest request, CancellationToken ct = default);
    Task<ApiResult<AvailabilityDto>> GetAvailabilityAsync(Guid doctorId, DateOnly date, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<AppointmentDto>>> GetAgendaAsync(Guid? doctorId, DateOnly date, CancellationToken ct = default);
    Task<ApiResult<AppointmentDto>> GetAppointmentAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<AppointmentDto>> CreateAppointmentAsync(CreateAppointmentRequest request, string idempotencyKey, CancellationToken ct = default);
    Task<ApiResult> CancelAppointmentAsync(Guid id, CancellationToken ct = default);
}

internal sealed class MediCoreApi(HttpClient http, IHttpContextAccessor accessor) : IMediCoreApi
{
    public async Task<ApiResult<LoginResponse>> LoginAsync(string email, string password, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(email, password), ct);
            if (response.IsSuccessStatusCode)
                return new ApiResult<LoginResponse>(ApiStatus.Ok, await response.Content.ReadFromJsonAsync<LoginResponse>(ct));
            return new ApiResult<LoginResponse>(Map(response.StatusCode, loginCall: true));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new ApiResult<LoginResponse>(ApiStatus.Unavailable);
        }
    }

    public async Task<RefreshResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        try
        {
            var response = await http.PostAsJsonAsync("api/auth/refresh", new RefreshRequest(refreshToken), ct);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<RefreshResponse>(ct) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    public async Task LogoutAsync(string accessToken, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/logout");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            // Si la API no responde, la cookie se elimina igualmente; la sesión caduca sola en Redis.
        }
    }

    public async Task<ApiResult<IReadOnlyList<UserDto>>> GetUsersAsync(CancellationToken ct = default)
        => AsReadOnly(await CallAsync<List<UserDto>>(HttpMethod.Get, "api/users", null, ct: ct));

    public Task<ApiResult> ChangeRoleAsync(Guid userId, string role, CancellationToken ct = default)
        => CallAsync(HttpMethod.Put, $"api/users/{userId}/role", new ChangeRoleRequest(role), ct: ct);

    public Task<ApiResult<PagedResult<PatientDto>>> GetPatientsAsync(string? query, int page, int pageSize = 20, CancellationToken ct = default)
        => CallAsync<PagedResult<PatientDto>>(HttpMethod.Get, $"api/patients?q={Uri.EscapeDataString(query ?? "")}&page={page}&pageSize={pageSize}", null, ct: ct);

    public Task<ApiResult<PatientDto>> GetPatientAsync(Guid id, CancellationToken ct = default)
        => CallAsync<PatientDto>(HttpMethod.Get, $"api/patients/{id}", null, ct: ct);

    public Task<ApiResult<PatientDto>> SavePatientAsync(Guid? id, PatientRequest request, CancellationToken ct = default)
        => id is null
            ? CallAsync<PatientDto>(HttpMethod.Post, "api/patients", request, ct: ct)
            : CallAsync<PatientDto>(HttpMethod.Put, $"api/patients/{id}", request, ct: ct);

    public async Task<ApiResult<IReadOnlyList<DoctorDto>>> GetDoctorsAsync(string? query, CancellationToken ct = default)
        => AsReadOnly(await CallAsync<List<DoctorDto>>(HttpMethod.Get, $"api/doctors?q={Uri.EscapeDataString(query ?? "")}", null, ct: ct));

    public Task<ApiResult<DoctorDto>> GetDoctorAsync(Guid id, CancellationToken ct = default)
        => CallAsync<DoctorDto>(HttpMethod.Get, $"api/doctors/{id}", null, ct: ct);

    public Task<ApiResult<DoctorDto>> GetMyDoctorAsync(CancellationToken ct = default)
        => CallAsync<DoctorDto>(HttpMethod.Get, "api/doctors/me", null, ct: ct);

    public Task<ApiResult<DoctorDto>> SaveDoctorAsync(Guid? id, DoctorRequest request, CancellationToken ct = default)
        => id is null
            ? CallAsync<DoctorDto>(HttpMethod.Post, "api/doctors", request, ct: ct)
            : CallAsync<DoctorDto>(HttpMethod.Put, $"api/doctors/{id}", request, ct: ct);

    public Task<ApiResult<ScheduleDto>> GetScheduleAsync(Guid doctorId, CancellationToken ct = default)
        => CallAsync<ScheduleDto>(HttpMethod.Get, $"api/doctors/{doctorId}/schedule", null, ct: ct);

    public Task<ApiResult<ScheduleDto>> SaveScheduleAsync(Guid doctorId, ScheduleRequest request, CancellationToken ct = default)
        => CallAsync<ScheduleDto>(HttpMethod.Put, $"api/doctors/{doctorId}/schedule", request, ct: ct);

    public Task<ApiResult<AvailabilityDto>> GetAvailabilityAsync(Guid doctorId, DateOnly date, CancellationToken ct = default)
        => CallAsync<AvailabilityDto>(HttpMethod.Get, $"api/doctors/{doctorId}/availability?date={date:yyyy-MM-dd}", null, ct: ct);

    public async Task<ApiResult<IReadOnlyList<AppointmentDto>>> GetAgendaAsync(Guid? doctorId, DateOnly date, CancellationToken ct = default)
        => AsReadOnly(await CallAsync<List<AppointmentDto>>(HttpMethod.Get,
            $"api/appointments?date={date:yyyy-MM-dd}{(doctorId is null ? "" : $"&doctorId={doctorId}")}", null, ct: ct));

    public Task<ApiResult<AppointmentDto>> GetAppointmentAsync(Guid id, CancellationToken ct = default)
        => CallAsync<AppointmentDto>(HttpMethod.Get, $"api/appointments/{id}", null, ct: ct);

    public Task<ApiResult<AppointmentDto>> CreateAppointmentAsync(CreateAppointmentRequest request, string idempotencyKey, CancellationToken ct = default)
        => CallAsync<AppointmentDto>(HttpMethod.Post, "api/appointments", request, idempotencyKey, ct);

    public Task<ApiResult> CancelAppointmentAsync(Guid id, CancellationToken ct = default)
        => CallAsync(HttpMethod.Post, $"api/appointments/{id}/cancel", null, ct: ct);

    private static ApiResult<IReadOnlyList<T>> AsReadOnly<T>(ApiResult<List<T>> result)
        => new(result.Status, result.Value, result.Message);

    private async Task<ApiResult> CallAsync(HttpMethod method, string url, object? body, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var response = await SendAsync(method, url, body, idempotencyKey, ct);
        if (response is null) return new ApiResult(ApiStatus.Unavailable);
        return response.IsSuccessStatusCode ? new ApiResult(ApiStatus.Ok) : new ApiResult(Map(response.StatusCode), await ReadMessageAsync(response, ct));
    }

    private async Task<ApiResult<T>> CallAsync<T>(HttpMethod method, string url, object? body, string? idempotencyKey = null, CancellationToken ct = default)
    {
        var response = await SendAsync(method, url, body, idempotencyKey, ct);
        if (response is null) return new ApiResult<T>(ApiStatus.Unavailable);
        if (!response.IsSuccessStatusCode) return new ApiResult<T>(Map(response.StatusCode), default, await ReadMessageAsync(response, ct));
        return new ApiResult<T>(ApiStatus.Ok, await response.Content.ReadFromJsonAsync<T>(ct));
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string url, object? body, string? idempotencyKey, CancellationToken ct)
    {
        var token = await accessor.HttpContext!.GetTokenAsync("access_token");
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body, body.GetType()) };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (idempotencyKey is not null)
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Extrae el mensaje en español de una respuesta ProblemDetails de la API (el primer error de validación o el detalle).</summary>
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

    private static ApiStatus Map(HttpStatusCode code, bool loginCall = false) => code switch
    {
        HttpStatusCode.Unauthorized => loginCall ? ApiStatus.InvalidCredentials : ApiStatus.Unauthorized,
        HttpStatusCode.Forbidden => ApiStatus.Forbidden,
        HttpStatusCode.NotFound => ApiStatus.NotFound,
        HttpStatusCode.Conflict => ApiStatus.Conflict,
        HttpStatusCode.BadRequest => ApiStatus.BadRequest,
        HttpStatusCode.TooManyRequests => ApiStatus.RateLimited,
        _ => ApiStatus.Unavailable,
    };
}
