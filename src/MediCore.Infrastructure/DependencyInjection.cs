using MediCore.Domain.Identity;
using MediCore.Infrastructure.Auditing;
using MediCore.Infrastructure.Caching;
using MediCore.Infrastructure.Identity;
using MediCore.Infrastructure.Messaging;
using MediCore.Infrastructure.Persistence;
using MediCore.Infrastructure.Scheduling;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MediCore.Infrastructure;

public static class DependencyInjection
{
    public const string PostgresConnectionName = "Postgres";
    public const string RedisConnectionName = "Redis";

    public static IServiceCollection AddMediCoreInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Las cadenas se leen al usarse, no al arrancar: la API puede iniciar (y responder /api/health)
        // aunque las dependencias todavía no estén configuradas.
        services.AddDbContext<MediCoreDbContext>((_, options) =>
        {
            var connectionString = configuration.GetConnectionString(PostgresConnectionName)
                ?? throw new InvalidOperationException(
                    "Falta la cadena de conexión 'ConnectionStrings:Postgres'. Defínela con user-secrets o con la variable de entorno ConnectionStrings__Postgres (ver README).");
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(MediCoreDbContext).Assembly.FullName));
        });

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

        services.AddSingleton(_ => new Lazy<IConnectionMultiplexer>(() =>
        {
            var connectionString = configuration.GetConnectionString(RedisConnectionName)
                ?? throw new InvalidOperationException(
                    "Falta la cadena de conexión 'ConnectionStrings:Redis'. Defínela con user-secrets o con la variable de entorno ConnectionStrings__Redis (ver README).");
            var options = ConfigurationOptions.Parse(connectionString);
            options.AbortOnConnectFail = false; // permite arrancar sin Redis; los comandos fallan con excepción
            options.ConnectTimeout = 2000;
            options.SyncTimeout = 2000;
            options.AsyncTimeout = 2000;
            return ConnectionMultiplexer.Connect(options);
        }));
        services.AddSingleton<ISessionStore, RedisSessionStore>();
        services.AddSingleton<ICacheService, RedisCacheService>();
        services.AddSingleton<IClinicClock, ClinicClock>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();

        services.AddScoped<IAuditLogger, AuditLogger>();

        // Mensajería (RabbitMQ) con outbox transaccional: el publicador y el consumidor corren en segundo plano.
        services.AddOptions<MessagingOptions>().Bind(configuration.GetSection(MessagingOptions.SectionName));
        services.AddSingleton<IRabbitMqConnection, RabbitMqConnection>();
        services.AddScoped<ILabResultReadyHandler, LabResultReadyHandler>();
        if (configuration.GetValue(MessagingOptions.SectionName + ":Enabled", true))
        {
            services.AddSingleton<OutboxPublisherService>();
            services.AddHostedService(sp => sp.GetRequiredService<OutboxPublisherService>());
            services.AddHostedService<NotificationConsumerService>();
        }

        return services;
    }
}
