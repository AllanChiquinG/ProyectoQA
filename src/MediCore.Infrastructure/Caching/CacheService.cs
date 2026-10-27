using System.Text.Json;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace MediCore.Infrastructure.Caching;

/// <summary>
/// Cache en Redis para catálogos y consultas frecuentes. Falla abierto: si Redis no responde se registra una advertencia
/// y se consulta la base de datos, de modo que la indisponibilidad del cache no tumba la funcionalidad.
/// </summary>
public interface ICacheService
{
    /// <summary>Devuelve el valor cacheado (HIT) o lo calcula, lo guarda con el TTL indicado y lo devuelve (MISS).</summary>
    Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default);

    /// <summary>Invalida una clave. Si Redis no responde, el dato vencerá por TTL.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>Invalida todas las claves que empiezan con el prefijo. Si Redis no responde, vencerán por TTL.</summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}

public static class CacheKeys
{
    public const string DoctorCatalog = "medicore:cache:doctors:v1";

    private const string AvailabilityRoot = "medicore:cache:availability:v1:";

    /// <summary>Disponibilidad (horarios y ocupados) de un médico en una fecha.</summary>
    public static string Availability(Guid doctorId, DateOnly date) => $"{AvailabilityPrefix(doctorId)}{date:yyyy-MM-dd}";

    /// <summary>Todas las fechas cacheadas de un médico (para invalidar al cambiar su horario de atención).</summary>
    public static string AvailabilityPrefix(Guid doctorId) => $"{AvailabilityRoot}{doctorId:N}:";
}

internal sealed class RedisCacheService(Lazy<IConnectionMultiplexer> redis, ILogger<RedisCacheService> logger) : ICacheService
{
    public async Task<T> GetOrCreateAsync<T>(string key, TimeSpan ttl, Func<CancellationToken, Task<T>> factory, CancellationToken ct = default)
    {
        try
        {
            var cached = await redis.Value.GetDatabase().StringGetAsync(key);
            if (cached.HasValue)
            {
                var value = JsonSerializer.Deserialize<T>((string)cached!);
                if (value is not null) return value;
            }
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Cache no disponible al leer {Key}; se consulta la base de datos.", key);
            return await factory(ct);
        }

        var created = await factory(ct);
        try
        {
            await redis.Value.GetDatabase().StringSetAsync(key, JsonSerializer.Serialize(created), ttl);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "Cache no disponible al guardar {Key}.", key);
        }

        return created;
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await redis.Value.GetDatabase().KeyDeleteAsync(key);
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "No se pudo invalidar {Key}; vencerá por TTL.", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            var multiplexer = redis.Value;
            var db = multiplexer.GetDatabase();
            foreach (var endpoint in multiplexer.GetEndPoints())
            {
                var server = multiplexer.GetServer(endpoint);
                if (server.IsReplica || !server.IsConnected) continue;
                var keys = server.KeysAsync(pattern: prefix + "*", pageSize: 250);
                await foreach (var key in keys.WithCancellation(ct))
                    await db.KeyDeleteAsync(key);
            }
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            logger.LogWarning(ex, "No se pudo invalidar el prefijo {Prefix}; vencerá por TTL.", prefix);
        }
    }

    // La cadena de conexión ausente (InvalidOperationException) también cuenta como cache no disponible.
    private static bool IsRedisFailure(Exception ex) => ex is RedisException or TimeoutException or InvalidOperationException;
}
