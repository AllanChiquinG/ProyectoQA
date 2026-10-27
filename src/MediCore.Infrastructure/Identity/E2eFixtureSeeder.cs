using MediCore.Domain.Doctors;
using MediCore.Domain.Identity;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Infrastructure.Identity;

/// <summary>
/// Solo Development: dos médicos ficticios, con su cuenta de acceso, dedicados a las pruebas end-to-end (Cypress). Así las pruebas
/// no necesitan tocar la agenda ni los datos de los médicos de uso normal (Dra. Méndez y demás). Es idempotente: se ejecuta en cada arranque.
/// </summary>
public static class E2eFixtureSeeder
{
    public static readonly (string DoctorName, string Email)[] Doctors =
    [
        ("Dr. Cypress E2E", "e2e.medico@example.com"),
        ("Dra. Cypress Colega", "e2e.colega@example.com"),
    ];

    public static async Task SeedAsync(MediCoreDbContext db, IPasswordHasher<User> hasher, string password, CancellationToken ct = default)
    {
        foreach (var (doctorName, email) in Doctors)
        {
            var normalized = User.NormalizeEmail(email);
            var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
            if (user is null)
            {
                user = new User { Email = email, NormalizedEmail = normalized, DisplayName = doctorName, Role = UserRole.Medico, PasswordHash = "" };
                user.PasswordHash = hasher.HashPassword(user, password);
                db.Users.Add(user);
            }

            // El médico puede existir ya (lo crean las pruebas por la API): se vincula con su cuenta.
            var doctor = await db.Doctors.FirstOrDefaultAsync(d => d.FullName == doctorName, ct);
            if (doctor is null)
            {
                db.Doctors.Add(new Doctor { FullName = doctorName, Specialty = "Medicina general", Phone = "5550 0000", Email = email, UserId = user.Id });
            }
            else if (doctor.UserId is null)
            {
                doctor.UserId = user.Id;
            }
        }

        await db.SaveChangesAsync(ct);
    }
}
