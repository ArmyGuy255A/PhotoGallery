using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class PhotoVersionConfiguration : IEntityTypeConfiguration<PhotoVersion>
{
    public void Configure(EntityTypeBuilder<PhotoVersion> builder)
    {
        builder.HasKey(pv => pv.Id);

        builder.Property(pv => pv.StorageKey)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(pv => pv.Quality)
            .HasConversion<int>();

        builder.HasOne(pv => pv.Photo)
            .WithMany(p => p.PhotoVersions)
            .HasForeignKey(pv => pv.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pv => new { pv.PhotoId, pv.Quality }).IsUnique();
        builder.HasIndex(pv => pv.StorageKey).IsUnique();
    }
}
