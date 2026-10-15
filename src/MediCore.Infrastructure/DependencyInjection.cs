using MediCore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MediCore.Infrastructure;

public static class DependencyInjection
{
    public const string PostgresConnectionName = "Postgres";

    public static IServiceCollection AddMediCoreInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // La cadena se lee al resolver el contexto, no al arrancar: la API puede iniciar (y responder /api/health)
        // aunque la base de datos todavía no esté configurada.
        services.AddDbContext<MediCoreDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(PostgresConnectionName)
                ?? throw new InvalidOperationException(
                    "Falta la cadena de conexión 'ConnectionStrings:Postgres'. Defínela con user-secrets o con la variable de entorno ConnectionStrings__Postgres (ver README).");
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(MediCoreDbContext).Assembly.FullName));
        });
        return services;
    }
}
