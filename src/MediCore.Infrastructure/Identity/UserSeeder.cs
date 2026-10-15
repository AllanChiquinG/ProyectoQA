using MediCore.Domain.Identity;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Infrastructure.Identity;

/// <summary>Usuarios ficticios para desarrollo (nombres tomados del diseño de referencia). Solo se usa en Development.</summary>
public static class UserSeeder
{
    private static readonly (string Name, string Email, UserRole Role)[] DevUsers =
    [
        ("Elena Vargas", "elena.vargas@example.com", UserRole.Recepcion),
        ("Lucía Méndez", "lucia.mendez@example.com", UserRole.Medico),
        ("Daniela Reyes", "daniela.reyes@example.com", UserRole.Laboratorio),
        ("Administrador MediCore", "admin@example.com", UserRole.Administrador),
        ("Ana Sofía García", "ana.garcia@example.com", UserRole.Paciente),
    ];

    /// <returns>Cantidad de usuarios creados (0 si ya existían usuarios).</returns>
    public static async Task<int> SeedAsync(MediCoreDbContext db, IPasswordHasher<User> hasher, string password, CancellationToken ct = default)
    {
        if (await db.Users.AnyAsync(ct)) return 0;

        foreach (var (name, email, role) in DevUsers)
        {
            var user = new User { Email = email, NormalizedEmail = User.NormalizeEmail(email), DisplayName = name, Role = role, PasswordHash = "" };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
        }

        await db.SaveChangesAsync(ct);
        return DevUsers.Length;
    }
}
