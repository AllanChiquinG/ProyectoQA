using MediCore.Domain.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MediCore.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedOnAdd();
        builder.Property(x => x.Action).HasMaxLength(60).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(60);
        builder.Property(x => x.ResourceId).HasMaxLength(64);
        builder.Property(x => x.Outcome).HasMaxLength(20).IsRequired();
        builder.Property(x => x.IpAddress).HasMaxLength(45);
        builder.Property(x => x.Details).HasMaxLength(200);
        builder.HasIndex(x => x.OccurredAtUtc);
        builder.HasIndex(x => x.ActorUserId);
    }
}
