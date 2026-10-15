using System.Security.Cryptography;
using System.Text;
using StackExchange.Redis;

namespace MediCore.Infrastructure.Identity;

public enum RefreshStatus { Rotated, Invalid, ReuseDetected }

public record RefreshResult(RefreshStatus Status, Guid? UserId, string? SessionId, string? NewRefreshToken);

/// <summary>
/// Sesiones en Redis. Cada sesión tiene un identificador y un token de refresco de un solo uso que rota en cada
/// renovación. Reutilizar un token ya rotado indica robo y revoca toda la sesión. La vida de la sesión es absoluta.
/// </summary>
public interface ISessionStore
{
    Task<(string SessionId, string RefreshToken)> CreateAsync(Guid userId, TimeSpan lifetime, CancellationToken ct = default);
    Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default);
    Task<RefreshResult> RotateAsync(string refreshToken, CancellationToken ct = default);
    Task RevokeAsync(string sessionId, CancellationToken ct = default);
    Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default);
}

internal sealed class RedisSessionStore(Lazy<IConnectionMultiplexer> redis) : ISessionStore
{
    private static string SessionKey(string sessionId) => $"medicore:session:{sessionId}";
    private static string UserKey(Guid userId) => $"medicore:user-sessions:{userId:N}";

    // Compara el hash, rota el secreto y devuelve {estado, userId}. Todo atómico para evitar carreras.
    // Estados: 0 inválido, 1 rotado, 2 reutilización (se elimina la sesión).
    private const string RotateScript = """
        local h = redis.call('HGET', KEYS[1], 'refreshHash')
        if not h then return {0, ''} end
        local uid = redis.call('HGET', KEYS[1], 'userId')
        if h ~= ARGV[1] then
            redis.call('DEL', KEYS[1])
            return {2, uid}
        end
        redis.call('HSET', KEYS[1], 'refreshHash', ARGV[2])
        return {1, uid}
        """;

    private IDatabase Db => redis.Value.GetDatabase();

    public async Task<(string SessionId, string RefreshToken)> CreateAsync(Guid userId, TimeSpan lifetime, CancellationToken ct = default)
    {
        var sessionId = NewToken(16);
        var secret = NewToken(32);
        var key = SessionKey(sessionId);
        var db = Db;

        await db.HashSetAsync(key, [new HashEntry("userId", userId.ToString()), new HashEntry("refreshHash", Hash(secret))]);
        await db.KeyExpireAsync(key, lifetime);
        await db.SetAddAsync(UserKey(userId), sessionId);
        await db.KeyExpireAsync(UserKey(userId), lifetime + TimeSpan.FromMinutes(5), ExpireWhen.GreaterThanCurrentExpiry);

        return (sessionId, $"{sessionId}.{secret}");
    }

    public async Task<bool> ExistsAsync(string sessionId, CancellationToken ct = default)
        => await Db.KeyExistsAsync(SessionKey(sessionId));

    public async Task<RefreshResult> RotateAsync(string refreshToken, CancellationToken ct = default)
    {
        var parts = refreshToken.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
            return new RefreshResult(RefreshStatus.Invalid, null, null, null);

        var sessionId = parts[0];
        var newSecret = NewToken(32);
        var result = (RedisResult[]?)await Db.ScriptEvaluateAsync(
            RotateScript,
            [SessionKey(sessionId)],
            [Hash(parts[1]), Hash(newSecret)]);

        var status = (int)result![0];
        Guid? userId = Guid.TryParse((string?)result[1], out var parsed) ? parsed : null;
        return status switch
        {
            1 => new RefreshResult(RefreshStatus.Rotated, userId, sessionId, $"{sessionId}.{newSecret}"),
            2 => new RefreshResult(RefreshStatus.ReuseDetected, userId, sessionId, null),
            _ => new RefreshResult(RefreshStatus.Invalid, null, null, null),
        };
    }

    public async Task RevokeAsync(string sessionId, CancellationToken ct = default)
        => await Db.KeyDeleteAsync(SessionKey(sessionId));

    public async Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var db = Db;
        var sessions = await db.SetMembersAsync(UserKey(userId));
        if (sessions.Length > 0)
            await db.KeyDeleteAsync([.. sessions.Select(s => (RedisKey)SessionKey(s.ToString()))]);
        await db.KeyDeleteAsync(UserKey(userId));
    }

    private static string NewToken(int bytes) => Base64UrlEncode(RandomNumberGenerator.GetBytes(bytes));

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static string Base64UrlEncode(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
