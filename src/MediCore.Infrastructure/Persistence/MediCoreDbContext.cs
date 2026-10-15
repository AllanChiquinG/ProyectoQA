using MediCore.Domain.Messaging;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Infrastructure.Persistence;

public class MediCoreDbContext(DbContextOptions<MediCoreDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediCoreDbContext).Assembly);
}
