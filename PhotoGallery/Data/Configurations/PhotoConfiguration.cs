using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class PhotoConfiguration : IEntityTypeConfiguration<Photo>
{
    public void Configure(EntityTypeBuilder<Photo> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.FileName)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(p => p.StorageKey)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(p => p.UploadedBy)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(p => p.ProcessingStatus)
            .HasConversion<int>()
            .HasDefaultValue(PhotoProcessingStatus.Pending);

        builder.Property(p => p.RowVersion)
            .HasDefaultValue(new byte[] { 1 });

        builder.HasOne(p => p.Album)
            .WithMany(a => a.Photos)
            .HasForeignKey(p => p.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.PhotoVersions)
            .WithOne(pv => pv.Photo)
            .HasForeignKey(pv => pv.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.PhotoFiles)
            .WithOne(pf => pf.Photo)
            .HasForeignKey(pf => pf.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => p.AlbumId);
        builder.HasIndex(p => p.StorageKey).IsUnique();
        builder.HasIndex(p => p.ProcessingStatus);
    }
}
