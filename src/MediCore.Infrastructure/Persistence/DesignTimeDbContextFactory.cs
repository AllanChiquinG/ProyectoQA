using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MediCore.Infrastructure.Persistence;

/// <summary>
/// Solo para <c>dotnet ef</c>. Usa la variable <c>ConnectionStrings__Postgres</c> si existe (necesaria para
/// <c>database update</c>); si no, una cadena sin credenciales que basta para generar migraciones.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MediCoreDbContext>
{
    public MediCoreDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Database=medicore_design";
        var options = new DbContextOptionsBuilder<MediCoreDbContext>()
            .UseNpgsql(connectionString, o => o.MigrationsAssembly(typeof(MediCoreDbContext).Assembly.FullName))
            .Options;
        return new MediCoreDbContext(options);
    }
}
