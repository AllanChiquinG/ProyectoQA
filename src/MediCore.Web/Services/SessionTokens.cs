using System.Globalization;
using System.Security.Claims;
using MediCore.Contracts.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MediCore.Web.Services;

/// <summary>Construye la sesión de cookie del personal y renueva los tokens de la API antes de que venzan.</summary>
public static class SessionTokens
{
    public const string AccessToken = "access_token";
    public const string RefreshToken = "refresh_token";
    public const string ExpiresAt = "expires_at";

    /// <summary>La vida de la cookie coincide con la sesión absoluta de la API (Jwt:SessionLifetime, 8 h por defecto).</summary>
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(8);

    private static readonly TimeSpan RenewBefore = TimeSpan.FromSeconds(60);

    public static (ClaimsPrincipal Principal, AuthenticationProperties Properties) Create(LoginResponse login, TimeProvider clock)
    {
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, login.User.Id.ToString()),
                new Claim(ClaimTypes.Name, login.User.DisplayName),
                new Claim(ClaimTypes.Email, login.User.Email),
                new Claim(ClaimTypes.Role, login.User.Role),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        var now = clock.GetUtcNow();
        var properties = new AuthenticationProperties { IsPersistent = false, ExpiresUtc = now + CookieLifetime };
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = AccessToken, Value = login.AccessToken },
            new AuthenticationToken { Name = RefreshToken, Value = login.RefreshToken },
            new AuthenticationToken { Name = ExpiresAt, Value = (now.AddSeconds(login.ExpiresInSeconds)).ToString("o", CultureInfo.InvariantCulture) },
        ]);
        return (new ClaimsPrincipal(identity), properties);
    }

    /// <summary>Se ejecuta en cada petición con cookie: renueva el acceso si vence pronto; si la sesión ya no es válida, cierra la cookie.</summary>
    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        var expiresAtText = context.Properties.GetTokenValue(ExpiresAt);
        var refreshToken = context.Properties.GetTokenValue(RefreshToken);
        var clock = context.HttpContext.RequestServices.GetRequiredService<TimeProvider>();

        if (refreshToken is null || !DateTimeOffset.TryParse(expiresAtText, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var expiresAt))
        {
            await Reject(context);
            return;
        }

        var now = clock.GetUtcNow();
        if (expiresAt - now > RenewBefore)
            return;

        var api = context.HttpContext.RequestServices.GetRequiredService<IMediCoreApi>();
        var refreshed = await api.RefreshAsync(refreshToken, context.HttpContext.RequestAborted);
        if (refreshed is null)
        {
            await Reject(context);
            return;
        }

        context.Properties.UpdateTokenValue(AccessToken, refreshed.AccessToken);
        context.Properties.UpdateTokenValue(RefreshToken, refreshed.RefreshToken);
        context.Properties.UpdateTokenValue(ExpiresAt, now.AddSeconds(refreshed.ExpiresInSeconds).ToString("o", CultureInfo.InvariantCulture));
        context.ShouldRenew = true;
    }

    private static async Task Reject(CookieValidatePrincipalContext context)
    {
        context.RejectPrincipal();
        await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }
}
