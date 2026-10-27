using MediCore.Domain.Auditing;
using MediCore.Domain.Clinical;
using MediCore.Domain.Doctors;
using MediCore.Domain.Identity;
using MediCore.Domain.Laboratory;
using MediCore.Domain.Messaging;
using MediCore.Domain.Notifications;
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
    public DbSet<Consultation> Consultations => Set<Consultation>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<PrescriptionItem> PrescriptionItems => Set<PrescriptionItem>();
    public DbSet<LabResult> LabResults => Set<LabResult>();
    public DbSet<LabResultParameter> LabResultParameters => Set<LabResultParameter>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.ApplyConfigurationsFromAssembly(typeof(MediCoreDbContext).Assembly);
}
