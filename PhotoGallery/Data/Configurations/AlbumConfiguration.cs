using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class AlbumConfiguration : IEntityTypeConfiguration<Album>
{
    public void Configure(EntityTypeBuilder<Album> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Title)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(a => a.Description)
            .HasMaxLength(1000);

        builder.Property(a => a.CreatedBy)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(a => a.RowVersion)
            .HasDefaultValue(new byte[] { 1 });

        builder.HasOne(a => a.Owner)
            .WithMany(u => u.Albums)
            .HasForeignKey(a => a.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(a => a.Photos)
            .WithOne(p => p.Album)
            .HasForeignKey(p => p.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(a => a.AccessCodes)
            .WithOne(ac => ac.Album)
            .HasForeignKey(ac => ac.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.OwnerId);
        builder.HasIndex(a => a.CreatedDate);
    }
}
