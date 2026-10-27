namespace MediCore.Mobile.Core.Api;

/// <summary>Credenciales de sesión. Nunca incluye la contraseña: solo los tokens que emite la API.</summary>
public record StoredSession(string AccessToken, string RefreshToken, DateTime AccessExpiresAtUtc, string DisplayName);

/// <summary>Almacén de la sesión. En Android lo implementa el almacenamiento seguro (Keystore); en las pruebas, memoria.</summary>
public interface ISessionStore
{
    Task<StoredSession?> LoadAsync();
    Task SaveAsync(StoredSession session);
    Task ClearAsync();
}

public sealed class InMemorySessionStore : ISessionStore
{
    private StoredSession? _session;

    public Task<StoredSession?> LoadAsync() => Task.FromResult(_session);

    public Task SaveAsync(StoredSession session)
    {
        _session = session;
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        _session = null;
        return Task.CompletedTask;
    }
}
