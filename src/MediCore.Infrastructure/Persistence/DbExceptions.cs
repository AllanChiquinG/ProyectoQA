using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCore.Infrastructure.Persistence;

public static class DbExceptions
{
    /// <summary>Violación de una restricción única de PostgreSQL (código 23505).</summary>
    public static bool IsUniqueViolation(this DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>Violación de una restricción de exclusión (23P01): dos filas activas se solapan.</summary>
    public static bool IsExclusionViolation(this DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.ExclusionViolation };

    /// <summary>Abortos que PostgreSQL pide reintentar: deadlock (40P01) o fallo de serialización (40001).</summary>
    public static bool IsTransientConcurrencyFailure(this Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
            if (e is PostgresException { SqlState: PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure })
                return true;
        return false;
    }

    /// <summary>Nombre de la restricción o índice violado, si PostgreSQL lo informó.</summary>
    public static string? ConstraintName(this DbUpdateException exception)
        => (exception.InnerException as PostgresException)?.ConstraintName;
}
