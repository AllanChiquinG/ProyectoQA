using MediCore.Domain.Laboratory;
using MediCore.Domain.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Infrastructure.Persistence.Configurations;

internal sealed class LabResultConfiguration : IEntityTypeConfiguration<LabResult>
{
    public void Configure(EntityTypeBuilder<LabResult> builder)
    {
        builder.ToTable("lab_results");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ExamType).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Notes).HasMaxLength(1000).IsRequired();
        builder.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(x => x.Parameters).WithOne().HasForeignKey(x => x.LabResultId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(x => new { x.PatientId, x.PerformedOn });
        builder.HasIndex(x => x.CreatedAtUtc);
    }
}

internal sealed class LabResultParameterConfiguration : IEntityTypeConfiguration<LabResultParameter>
{
    public void Configure(EntityTypeBuilder<LabResultParameter> builder)
    {
        builder.ToTable("lab_result_parameters");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Value).HasMaxLength(40).IsRequired();
        builder.Property(x => x.Unit).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ReferenceRange).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.LabResultId, x.Position }).IsUnique();
    }
}

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Type).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(300).IsRequired();
        builder.HasOne(x => x.Patient).WithMany().HasForeignKey(x => x.PatientId).OnDelete(DeleteBehavior.Restrict);

        // Idempotencia del consumidor: reprocesar el mismo mensaje (o pedir de nuevo la notificación de un resultado ya notificado)
        // no puede crear una segunda notificación. Lo garantiza la base de datos aun con consumidores simultáneos.
        builder.HasIndex(x => new { x.Type, x.SourceId }).IsUnique();
        builder.HasIndex(x => x.EventId).IsUnique();
        builder.HasIndex(x => new { x.PatientId, x.CreatedAtUtc });
    }
}
