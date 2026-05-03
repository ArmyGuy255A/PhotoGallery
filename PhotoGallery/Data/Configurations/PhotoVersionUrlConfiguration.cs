using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class PhotoVersionUrlConfiguration : IEntityTypeConfiguration<PhotoVersionUrl>
{
    public void Configure(EntityTypeBuilder<PhotoVersionUrl> builder)
    {
        builder.HasKey(pvu => pvu.Id);

        builder.Property(pvu => pvu.PresignedUrl)
            .HasMaxLength(2000)
            .IsRequired();

        builder.Property(pvu => pvu.Quality)
            .HasConversion<int>();

        builder.Property(pvu => pvu.ExpiresAt)
            .HasColumnType("datetime");

        builder.Property(pvu => pvu.GeneratedAt)
            .HasColumnType("datetime");

        builder.HasOne(pvu => pvu.Photo)
            .WithMany(p => p.PhotoVersionUrls)
            .HasForeignKey(pvu => pvu.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Index for finding URLs to refresh (expires soon)
        builder.HasIndex(pvu => pvu.ExpiresAt);
        
        // Index for finding cached URLs by photo and quality
        builder.HasIndex(pvu => new { pvu.PhotoId, pvu.Quality }).IsUnique();
        
        // Index for finding active URLs
        builder.HasIndex(pvu => pvu.IsActive);
    }
}
