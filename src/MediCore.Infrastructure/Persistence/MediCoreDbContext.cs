using MediCore.Domain.Auditing;
using MediCore.Domain.Doctors;
using MediCore.Domain.Identity;
using MediCore.Domain.Messaging;
using MediCore.Domain.Patients;
using MediCore.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace MediCore.Infrastructure.Persistence;

public class MediCoreDbContext(DbContextOptions<MediCoreDbContext> options) : DbContext(options)
{
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<DoctorSchedule> DoctorSchedules => Set<DoctorSchedule>();
    public DbSet<Appointment> Appointments => Set<Appointment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediCoreDbContext).Assembly);
}
