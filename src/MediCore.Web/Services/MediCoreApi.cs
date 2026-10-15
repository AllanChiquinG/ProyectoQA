using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediCore.Contracts.Auth;
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
    {
        var response = await SendAsync(HttpMethod.Get, "api/users", null, ct);
        if (response is null) return new ApiResult<IReadOnlyList<UserDto>>(ApiStatus.Unavailable);
        if (!response.IsSuccessStatusCode) return new ApiResult<IReadOnlyList<UserDto>>(Map(response.StatusCode));
        var users = await response.Content.ReadFromJsonAsync<List<UserDto>>(ct) ?? [];
        return new ApiResult<IReadOnlyList<UserDto>>(ApiStatus.Ok, users);
    }

    public async Task<ApiResult> ChangeRoleAsync(Guid userId, string role, CancellationToken ct = default)
    {
        var response = await SendAsync(HttpMethod.Put, $"api/users/{userId}/role", JsonContent.Create(new ChangeRoleRequest(role)), ct);
        return response is null ? new ApiResult(ApiStatus.Unavailable)
            : new ApiResult(response.IsSuccessStatusCode ? ApiStatus.Ok : Map(response.StatusCode));
    }

    private async Task<HttpResponseMessage?> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken ct)
    {
        var context = accessor.HttpContext!;
        var token = await context.GetTokenAsync("access_token");
        using var request = new HttpRequestMessage(method, url) { Content = content };
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
