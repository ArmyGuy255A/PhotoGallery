using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.ActorUserId)
            .HasMaxLength(450);

        builder.Property(a => a.ActorEmail)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(a => a.Action)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(a => a.TargetType)
            .HasMaxLength(128);

        builder.Property(a => a.TargetId)
            .HasMaxLength(256);

        builder.Property(a => a.Timestamp)
            .HasColumnType("datetime");

        builder.HasIndex(a => a.Timestamp);
        builder.HasIndex(a => a.Action);
    }
}
