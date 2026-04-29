using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.FirstName).HasMaxLength(100);
        builder.Property(u => u.LastName).HasMaxLength(100);

        builder.HasMany(u => u.Albums)
            .WithOne(a => a.Owner)
            .HasForeignKey(a => a.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(u => u.AccessCodes)
            .WithOne()
            .HasForeignKey(ac => ac.CreatedBy)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(u => u.UserAccessLogs)
            .WithOne(ual => ual.User)
            .HasForeignKey(ual => ual.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
