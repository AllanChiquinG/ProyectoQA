using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MediCore.Infrastructure.Persistence;

public static class DbExceptions
{
    /// <summary>Violación de una restricción única de PostgreSQL (código 23505).</summary>
    public static bool IsUniqueViolation(this DbUpdateException exception)
        => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
