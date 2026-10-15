using System.Security.Claims;
using System.Text;
using MediCore.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace MediCore.Api.Security;

public record AccessToken(string Value, int ExpiresInSeconds);

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider clock)
{
    private readonly JwtOptions _options = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Create(User user, string sessionId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var expires = now + _options.AccessTokenLifetime;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expires,
            SigningCredentials = new SigningCredentials(SigningKey(_options), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [ClaimNames.Subject] = user.Id.ToString(),
                [ClaimNames.Email] = user.Email,
                [ClaimNames.Name] = user.DisplayName,
                [ClaimNames.Role] = user.Role.ToString(),
                [ClaimNames.SessionId] = sessionId,
            },
        };

        return new AccessToken(_handler.CreateToken(descriptor), (int)_options.AccessTokenLifetime.TotalSeconds);
    }

    public static SymmetricSecurityKey SigningKey(JwtOptions options) => new(Encoding.UTF8.GetBytes(options.SigningKey));
}

/// <summary>Nombres de claim usados en los tokens (sin el mapeo automático de Microsoft).</summary>
public static class ClaimNames
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string SessionId = "sid";
}

public static class PrincipalExtensions
{
    public static Guid? UserId(this ClaimsPrincipal principal)
        => Guid.TryParse(principal.FindFirstValue(ClaimNames.Subject), out var id) ? id : null;

    public static string? SessionId(this ClaimsPrincipal principal) => principal.FindFirstValue(ClaimNames.SessionId);
}
