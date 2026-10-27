namespace MediCore.Mobile.Core.Api;

public enum ApiStatus
{
    Ok,

    /// <summary>No se pudo llegar al servicio (sin red, servicio apagado o tiempo agotado).</summary>
    Offline,

    /// <summary>Correo o contraseña incorrectos (solo en el inicio de sesión).</summary>
    InvalidCredentials,

    /// <summary>La sesión ya no es válida y no se pudo renovar: hay que iniciar sesión de nuevo.</summary>
    SessionExpired,
    Forbidden,
    NotFound,

    /// <summary>Conflicto de negocio, por ejemplo un horario que otra persona ocupó antes.</summary>
    Conflict,

    /// <summary>La API rechazó los datos; <see cref="ApiResult.Message"/> trae el motivo en español.</summary>
    Invalid,
    RateLimited,
    Unavailable,
}

public record ApiResult(ApiStatus Status, string? Message = null)
{
    public bool IsOk => Status == ApiStatus.Ok;
}

public record ApiResult<T>(ApiStatus Status, T? Value = default, string? Message = null) : ApiResult(Status, Message);
