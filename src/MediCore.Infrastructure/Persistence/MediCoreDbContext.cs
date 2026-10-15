using MediCore.Domain.Auditing;
using MediCore.Domain.Identity;
using MediCore.Domain.Messaging;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Infrastructure.Persistence;

public class MediCoreDbContext(DbContextOptions<MediCoreDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediCoreDbContext).Assembly);
}
