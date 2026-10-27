using MediCore.Domain.Clinical;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Infrastructure.Persistence.Configurations;

internal sealed class ConsultationConfiguration : IEntityTypeConfiguration<Consultation>
{
    public void Configure(EntityTypeBuilder<Consultation> builder)
    {
        builder.ToTable("consultations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Reason).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.Diagnosis).HasMaxLength(500).IsRequired();

        builder.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Appointment).WithMany().HasForeignKey(x => x.AppointmentId).OnDelete(DeleteBehavior.Restrict);

        // Una consulta por cita: lo garantiza la base de datos aun con envíos simultáneos.
        builder.HasIndex(x => x.AppointmentId).IsUnique();
        builder.HasIndex(x => new { x.PatientId, x.DoctorId });
    }
}

internal sealed class PrescriptionConfiguration : IEntityTypeConfiguration<Prescription>
{
    public void Configure(EntityTypeBuilder<Prescription> builder)
    {
        builder.ToTable("prescriptions");
        builder.HasKey(x => x.Id);
        builder.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Doctor).WithMany().HasForeignKey(x => x.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Consultation).WithMany().HasForeignKey(x => x.ConsultationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.PrescriptionId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.PatientId, x.DoctorId });
        builder.HasIndex(x => x.ConsultationId);
    }
}

internal sealed class PrescriptionItemConfiguration : IEntityTypeConfiguration<PrescriptionItem>
{
    public void Configure(EntityTypeBuilder<PrescriptionItem> builder)
    {
        builder.ToTable("prescription_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Medication).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Dose).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Frequency).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Duration).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Instructions).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => new { x.PrescriptionId, x.Position }).IsUnique();
    }
}
