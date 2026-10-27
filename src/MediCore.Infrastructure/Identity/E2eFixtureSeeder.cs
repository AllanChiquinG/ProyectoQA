using MediCore.Domain.Doctors;
using MediCore.Domain.Identity;
using MediCore.Domain.Patients;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Infrastructure.Identity;

/// <summary>
/// Solo Development: dos médicos y un paciente ficticios, con su cuenta de acceso, dedicados a las pruebas end-to-end (Cypress). Así las pruebas
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

        await SeedPatientAccountAsync(db, hasher, password, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Correo de la cuenta de paciente de pruebas (para comprobar por la API las notificaciones y resultados del paciente).</summary>
    public const string PatientEmail = "e2e.paciente@example.com";

    /// <summary>Identificación del paciente de pruebas con cuenta propia; coincide con el patrón CYP-E2E-* que limpian las pruebas.</summary>
    public const string PatientIdentification = "CYP-E2E-004";

    private static async Task SeedPatientAccountAsync(MediCoreDbContext db, IPasswordHasher<User> hasher, string password, CancellationToken ct)
    {
        var normalized = User.NormalizeEmail(PatientEmail);
        var user = await db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
        if (user is null)
        {
            user = new User { Email = PatientEmail, NormalizedEmail = normalized, DisplayName = "Cypress Paciente App", Role = UserRole.Paciente, PasswordHash = "" };
            user.PasswordHash = hasher.HashPassword(user, password);
            db.Users.Add(user);
        }

        var patient = await db.Patients.FirstOrDefaultAsync(p => p.NormalizedIdentification == PatientIdentification, ct);
        if (patient is null)
        {
            db.Patients.Add(new Patient
            {
                FullName = "Cypress Paciente App", IdentificationNumber = PatientIdentification, NormalizedIdentification = PatientIdentification,
                BirthDate = new DateOnly(1992, 3, 15), Phone = "5550 0000", Email = PatientEmail, UserId = user.Id,
            });
        }
        else if (patient.UserId is null)
        {
            patient.UserId = user.Id;
        }
    }
}
