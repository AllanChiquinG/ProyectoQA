using MediCore.Domain.Identity;
using MediCore.Infrastructure.Identity;
using MediCore.Infrastructure.Persistence;
using MediCore.Infrastructure.Scheduling;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Security;

/// <summary>
/// Solo en Development: aplica migraciones y crea usuarios ficticios si la tabla está vacía y existe
/// <c>Seed:DevPassword</c> (user-secrets). Nunca falla el arranque: si no hay base de datos, lo registra y sigue.
/// </summary>
public static class DevelopmentBootstrap
{
    public static Task RunAsync(WebApplication app) => RunAsync(app, migrate: true);

    /// <summary>
    /// Siembra de datos ficticios sin migrar (ambiente QA: las migraciones las aplica el contenedor "migrate" antes de arrancar la API).
    /// Se invoca con <c>MediCore.Api.dll --seed-qa</c> y solo se permite en Development y QA.
    /// </summary>
    public static Task SeedAsync(WebApplication app) => RunAsync(app, migrate: false);

    private static async Task RunAsync(WebApplication app, bool migrate)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevelopmentBootstrap");
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
            if (migrate) await db.Database.MigrateAsync();

            var password = app.Configuration["Seed:DevPassword"];
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogInformation("Seed:DevPassword no está definido: no se crean usuarios de desarrollo.");
            }
            else
            {
                var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>();
                var created = await UserSeeder.SeedAsync(db, hasher, password);
                if (created > 0) logger.LogInformation("Se crearon {Count} usuarios ficticios de desarrollo.", created);
            }

            // Primero los médicos y pacientes de demostración y después los de las pruebas end-to-end: el sembrado de demostración solo crea
            // médicos y pacientes si todavía no hay ninguno, así que en una base nueva debe ir antes.
            await ClinicalDemoSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IClinicClock>());

            if (!string.IsNullOrWhiteSpace(password))
            {
                // Médicos y paciente dedicados a las pruebas end-to-end (no se tocan los médicos de uso normal).
                await E2eFixtureSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(), password);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo preparar la base de datos de desarrollo (¿está levantado docker compose y configurada la cadena de conexión?).");
        }
    }
}
