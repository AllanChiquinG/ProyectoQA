using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediCore.Contracts;
using MediCore.Contracts.Auth;
using MediCore.Contracts.Doctors;
using MediCore.Contracts.Patients;
using MediCore.Contracts.Users;
using Microsoft.AspNetCore.Authentication;

namespace MediCore.Web.Services;

public enum ApiStatus { Ok, InvalidCredentials, Unauthorized, Forbidden, NotFound, Conflict, BadRequest, RateLimited, Unavailable }

public record ApiResult(ApiStatus Status)
{
    public bool IsOk => Status == ApiStatus.Ok;
}

public record ApiResult<T>(ApiStatus Status, T? Value = default) : ApiResult(Status);

/// <summary>Cliente de la API de MediCore. Las llamadas autenticadas usan el token guardado en la cookie del usuario.</summary>
public interface IMediCoreApi
{
    Task<ApiResult<LoginResponse>> LoginAsync(string email, string password, CancellationToken ct = default);
    Task<RefreshResponse?> RefreshAsync(string refreshToken, CancellationToken ct = default);
    Task LogoutAsync(string accessToken, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<UserDto>>> GetUsersAsync(CancellationToken ct = default);
    Task<ApiResult> ChangeRoleAsync(Guid userId, string role, CancellationToken ct = default);

    Task<ApiResult<PagedResult<PatientDto>>> GetPatientsAsync(string? query, int page, CancellationToken ct = default);
    Task<ApiResult<PatientDto>> GetPatientAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<PatientDto>> SavePatientAsync(Guid? id, PatientRequest request, CancellationToken ct = default);

    Task<ApiResult<IReadOnlyList<DoctorDto>>> GetDoctorsAsync(string? query, CancellationToken ct = default);
    Task<ApiResult<DoctorDto>> GetDoctorAsync(Guid id, CancellationToken ct = default);
    Task<ApiResult<DoctorDto>> SaveDoctorAsync(Guid? id, DoctorRequest request, CancellationToken ct = default);
}

internal sealed class MediCoreApi(HttpClient http, IHttpContextAccessor accessor) : IMediCoreApi
{
    public const int PageSize = 20;

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
    {
        var result = await CallAsync<List<UserDto>>(HttpMethod.Get, "api/users", null, ct);
        return new ApiResult<IReadOnlyList<UserDto>>(result.Status, result.Value);
    }

    public Task<ApiResult> ChangeRoleAsync(Guid userId, string role, CancellationToken ct = default)
        => CallAsync(HttpMethod.Put, $"api/users/{userId}/role", new ChangeRoleRequest(role), ct);

    public Task<ApiResult<PagedResult<PatientDto>>> GetPatientsAsync(string? query, int page, CancellationToken ct = default)
        => CallAsync<PagedResult<PatientDto>>(HttpMethod.Get, $"api/patients?q={Uri.EscapeDataString(query ?? "")}&page={page}&pageSize={PageSize}", null, ct);

    public Task<ApiResult<PatientDto>> GetPatientAsync(Guid id, CancellationToken ct = default)
        => CallAsync<PatientDto>(HttpMethod.Get, $"api/patients/{id}", null, ct);

    public Task<ApiResult<PatientDto>> SavePatientAsync(Guid? id, PatientRequest request, CancellationToken ct = default)
        => id is null
            ? CallAsync<PatientDto>(HttpMethod.Post, "api/patients", request, ct)
            : CallAsync<PatientDto>(HttpMethod.Put, $"api/patients/{id}", request, ct);

    public async Task<ApiResult<IReadOnlyList<DoctorDto>>> GetDoctorsAsync(string? query, CancellationToken ct = default)
    {
        var result = await CallAsync<List<DoctorDto>>(HttpMethod.Get, $"api/doctors?q={Uri.EscapeDataString(query ?? "")}", null, ct);
        return new ApiResult<IReadOnlyList<DoctorDto>>(result.Status, result.Value);
    }

    public Task<ApiResult<DoctorDto>> GetDoctorAsync(Guid id, CancellationToken ct = default)
        => CallAsync<DoctorDto>(HttpMethod.Get, $"api/doctors/{id}", null, ct);

    public Task<ApiResult<DoctorDto>> SaveDoctorAsync(Guid? id, DoctorRequest request, CancellationToken ct = default)
        => id is null
            ? CallAsync<DoctorDto>(HttpMethod.Post, "api/doctors", request, ct)
            : CallAsync<DoctorDto>(HttpMethod.Put, $"api/doctors/{id}", request, ct);

    private async Task<ApiResult> CallAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var response = await SendAsync(method, url, body, ct);
        return response is null ? new ApiResult(ApiStatus.Unavailable)
            : new ApiResult(response.IsSuccessStatusCode ? ApiStatus.Ok : Map(response.StatusCode));
    }

    private async Task<ApiResult<T>> CallAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var response = await SendAsync(method, url, body, ct);
        if (response is null) return new ApiResult<T>(ApiStatus.Unavailable);
        if (!response.IsSuccessStatusCode) return new ApiResult<T>(Map(response.StatusCode));
        return new ApiResult<T>(ApiStatus.Ok, await response.Content.ReadFromJsonAsync<T>(ct));
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        var token = await accessor.HttpContext!.GetTokenAsync("access_token");
        using var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body, body.GetType()) };
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try
        {
            return await http.SendAsync(request, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
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
