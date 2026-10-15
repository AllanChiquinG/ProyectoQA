using System.ComponentModel.DataAnnotations;

namespace MediCore.Api.Security;

public class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "medicore-api";

    [Required]
    public string Audience { get; set; } = "medicore";

    /// <summary>Clave de firma HMAC. No tiene valor por defecto: debe venir de user-secrets o del entorno (mínimo 32 bytes).</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = "";

    /// <summary>Vida del token de acceso. Corta; la renovación se hace con el token de refresco.</summary>
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Vida absoluta de la sesión en Redis (y del token de refresco).</summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(8);
}
