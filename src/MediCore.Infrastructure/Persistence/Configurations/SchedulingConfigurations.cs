using MediCore.Domain.Doctors;
using MediCore.Domain.Patients;
using MediCore.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Infrastructure.Persistence.Configurations;

internal sealed class DoctorScheduleConfiguration : IEntityTypeConfiguration<DoctorSchedule>
{
    public void Configure(EntityTypeBuilder<DoctorSchedule> builder)
    {
        builder.ToTable("doctor_schedules");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.DoctorId).IsUnique();
        builder.HasOne<Doctor>().WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class AppointmentConfiguration : IEntityTypeConfiguration<Appointment>
{
    public void Configure(EntityTypeBuilder<Appointment> builder)
    {
        builder.ToTable("appointments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StartsAtLocal).HasColumnType("timestamp without time zone");
        builder.Property(x => x.EndsAtLocal).HasColumnType("timestamp without time zone");
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(120);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(64);

        builder.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.DoctorId, x.StartsAtLocal });
        builder.HasIndex(x => new { x.PatientId, x.StartsAtLocal });

        // Un mismo usuario no puede reutilizar una clave con dos citas distintas.
        builder.HasIndex(x => new { x.CreatedByUserId, x.IdempotencyKey }).IsUnique()
            .HasFilter("\"IdempotencyKey\" IS NOT NULL").HasDatabaseName(SchedulingConstraints.IdempotencyIndex);

        // Las restricciones de exclusión (no solapar citas activas del mismo médico ni del mismo paciente) no existen en EF:
        // se crean con SQL en la migración "AgendaYCitas".
    }
}
