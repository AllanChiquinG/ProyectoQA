namespace MediCore.Domain.Identity;

public class User
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Email { get; set; }

    /// <summary>Correo en minúsculas y sin espacios; es la clave única de acceso.</summary>
    public required string NormalizedEmail { get; set; }

    public required string DisplayName { get; set; }
    public required string PasswordHash { get; set; }
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; private set; }
    public DateTime? LockoutEndUtc { get; private set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public bool IsLockedOut(DateTime nowUtc) => LockoutEndUtc is { } end && end > nowUtc;

    /// <summary>Cuenta un intento fallido; al llegar al máximo bloquea la cuenta y reinicia el contador.</summary>
    public void RegisterFailedLogin(DateTime nowUtc)
    {
        // Un bloqueo ya vencido no debe arrastrar intentos anteriores.
        if (LockoutEndUtc is { } end && end <= nowUtc)
        {
            LockoutEndUtc = null;
            FailedLoginCount = 0;
        }

        FailedLoginCount++;
        if (FailedLoginCount >= MaxFailedAttempts)
        {
            LockoutEndUtc = nowUtc + LockoutDuration;
            FailedLoginCount = 0;
        }
    }

    public void RegisterSuccessfulLogin()
    {
        FailedLoginCount = 0;
        LockoutEndUtc = null;
    }
}
