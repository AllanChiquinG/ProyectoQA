using MediCore.Domain.Identity;
using MediCore.Infrastructure.Identity;
using MediCore.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Api.Security;

/// <summary>
/// Solo en Development: aplica migraciones y crea usuarios ficticios si la tabla está vacía y existe
/// <c>Seed:DevPassword</c> (user-secrets). Nunca falla el arranque: si no hay base de datos, lo registra y sigue.
/// </summary>
public static class DevelopmentBootstrap
{
    public static async Task RunAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DevelopmentBootstrap");
        try
        {
            var db = scope.ServiceProvider.GetRequiredService<MediCoreDbContext>();
            await db.Database.MigrateAsync();

            var password = app.Configuration["Seed:DevPassword"];
            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogInformation("Seed:DevPassword no está definido: no se crean usuarios de desarrollo.");
                return;
            }

            var created = await UserSeeder.SeedAsync(db, scope.ServiceProvider.GetRequiredService<IPasswordHasher<User>>(), password);
            if (created > 0) logger.LogInformation("Se crearon {Count} usuarios ficticios de desarrollo.", created);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "No se pudo preparar la base de datos de desarrollo (¿está levantado docker compose y configurada la cadena de conexión?).");
        }
    }
}
