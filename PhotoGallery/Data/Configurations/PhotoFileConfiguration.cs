using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class PhotoFileConfiguration : IEntityTypeConfiguration<PhotoFile>
{
    public void Configure(EntityTypeBuilder<PhotoFile> builder)
    {
        builder.HasKey(pf => pf.Id);

        builder.Property(pf => pf.Quality)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(pf => pf.BlobPath)
            .HasMaxLength(1000)
            .IsRequired();

        builder.HasOne(pf => pf.Photo)
            .WithMany(p => p.PhotoFiles)
            .HasForeignKey(pf => pf.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes for common queries
        builder.HasIndex(pf => pf.PhotoId);
        builder.HasIndex(pf => new { pf.PhotoId, pf.Quality }).IsUnique();
    }
}
