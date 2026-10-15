using MediCore.Domain.Doctors;
using MediCore.Domain.Patients;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Infrastructure.Persistence.Configurations;

internal sealed class PatientConfiguration : IEntityTypeConfiguration<Patient>
{
    public void Configure(EntityTypeBuilder<Patient> builder)
    {
        builder.ToTable("patients");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FullName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.IdentificationNumber).HasMaxLength(30).IsRequired();
        builder.Property(x => x.NormalizedIdentification).HasMaxLength(30).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(254).IsRequired();

        // La unicidad la garantiza la base de datos: dos altas simultáneas con la misma identificación no pueden coexistir.
        builder.HasIndex(x => x.NormalizedIdentification).IsUnique();
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
        builder.HasIndex(x => x.FullName);
    }
}

internal sealed class DoctorConfiguration : IEntityTypeConfiguration<Doctor>
{
    public void Configure(EntityTypeBuilder<Doctor> builder)
    {
        builder.ToTable("doctors");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.FullName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Specialty).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(254).IsRequired();
        builder.HasIndex(x => x.UserId).IsUnique().HasFilter("\"UserId\" IS NOT NULL");
    }
}
