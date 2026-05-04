using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class DownloadConfiguration : IEntityTypeConfiguration<Download>
{
    public void Configure(EntityTypeBuilder<Download> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.IpHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.Quality)
            .HasConversion<int>();

        builder.Property(d => d.DownloadedAt)
            .HasColumnType("datetime");

        builder.HasOne(d => d.Photo)
            .WithMany()
            .HasForeignKey(d => d.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.AccessCode)
            .WithMany()
            .HasForeignKey(d => d.AccessCodeId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        // Indexes for analytics queries
        builder.HasIndex(d => d.PhotoId);
        builder.HasIndex(d => d.AccessCodeId);
        builder.HasIndex(d => d.DownloadedAt);
    }
}
