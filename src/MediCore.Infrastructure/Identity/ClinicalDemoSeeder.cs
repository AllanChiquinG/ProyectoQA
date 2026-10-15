using MediCore.Domain.Doctors;
using MediCore.Domain.Patients;
using MediCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Infrastructure.Identity;

/// <summary>Pacientes y médicos ficticios para desarrollo (los del diseño de referencia). Solo se usa en Development.</summary>
public static class ClinicalDemoSeeder
{
    public static async Task SeedAsync(MediCoreDbContext db, CancellationToken ct = default)
    {
        if (!await db.Doctors.AnyAsync(ct))
        {
            var lucia = await db.Users.Where(u => u.NormalizedEmail == "lucia.mendez@example.com").Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);
            db.Doctors.AddRange(
                new Doctor { FullName = "Dra. Lucía Méndez", Specialty = "Medicina general", Phone = "5550 1420", Email = "lucia.mendez@example.com", UserId = lucia },
                new Doctor { FullName = "Dr. Alejandro Ríos", Specialty = "Cardiología", Phone = "5550 2360", Email = "alejandro.rios@example.com" },
                new Doctor { FullName = "Dra. Valeria Castro", Specialty = "Medicina interna", Phone = "5550 3710", Email = "valeria.castro@example.com" });
        }

        if (!await db.Patients.AnyAsync(ct))
        {
            var ana = await db.Users.Where(u => u.NormalizedEmail == "ana.garcia@example.com").Select(u => (Guid?)u.Id).SingleOrDefaultAsync(ct);
            db.Patients.AddRange(
                NewPatient("Ana Sofía García", "PAC-001", new DateOnly(1994, 5, 18), "5550 2841", "ana.garcia@example.com", ana),
                NewPatient("Carlos Daniel López", "PAC-002", new DateOnly(1985, 2, 9), "5550 7392", "carlos.lopez@example.com"),
                NewPatient("María Fernanda Ruiz", "PAC-003", new DateOnly(1990, 8, 22), "5550 1964", "maria.ruiz@example.com"),
                NewPatient("José Andrés Morales", "PAC-004", new DateOnly(1978, 11, 4), "5550 4820", "jose.morales@example.com"));
        }

        await db.SaveChangesAsync(ct);
    }

    private static Patient NewPatient(string name, string id, DateOnly birth, string phone, string email, Guid? userId = null) => new()
    {
        FullName = name,
        IdentificationNumber = id,
        NormalizedIdentification = Patient.NormalizeIdentification(id),
        BirthDate = birth,
        Phone = phone,
        Email = email,
        UserId = userId,
    };
}
