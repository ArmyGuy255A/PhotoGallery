using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class UserAccessLogConfiguration : IEntityTypeConfiguration<UserAccessLog>
{
    public void Configure(EntityTypeBuilder<UserAccessLog> builder)
    {
        builder.HasKey(ual => ual.Id);

        builder.Property(ual => ual.IpAddress)
            .HasMaxLength(45);

        builder.HasOne(ual => ual.User)
            .WithMany(u => u.UserAccessLogs)
            .HasForeignKey(ual => ual.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(ual => ual.AccessCode)
            .WithMany(ac => ac.UserAccessLogs)
            .HasForeignKey(ual => ual.AccessCodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ual => ual.AccessCodeId);
        builder.HasIndex(ual => ual.UserId);
        builder.HasIndex(ual => ual.AccessDate);
    }
}
