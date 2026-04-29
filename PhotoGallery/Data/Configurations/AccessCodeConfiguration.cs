using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class AccessCodeConfiguration : IEntityTypeConfiguration<AccessCode>
{
    public void Configure(EntityTypeBuilder<AccessCode> builder)
    {
        builder.HasKey(ac => ac.Id);

        builder.Property(ac => ac.Code)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(ac => ac.CreatedBy)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(ac => ac.RowVersion)
            .HasDefaultValue(new byte[] { 1 });

        builder.HasOne(ac => ac.Album)
            .WithMany(a => a.AccessCodes)
            .HasForeignKey(ac => ac.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(ac => ac.UserAccessLogs)
            .WithOne(ual => ual.AccessCode)
            .HasForeignKey(ual => ual.AccessCodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ac => ac.Code).IsUnique();
        builder.HasIndex(ac => ac.AlbumId);
        builder.HasIndex(ac => ac.ExpirationDate);
    }
}
