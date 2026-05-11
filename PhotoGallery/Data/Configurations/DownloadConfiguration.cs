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

        // SqlServer rejects multiple cascade paths into the same table on
        // a single delete. Photos.UserId cascades on User delete; AccessCodes
        // cascade from Album → User. Both paths can reach Downloads, so we
        // can't cascade Downloads → Photos *and* SetNull Downloads →
        // AccessCodes without SqlServer raising:
        //   "Introducing FOREIGN KEY constraint 'FK_Downloads_Photos_PhotoId'
        //    on table 'Downloads' may cause cycles or multiple cascade paths."
        //
        // Restrict on Photos forces the caller to delete Downloads first
        // (the app's purge path already does this); SetNull on the optional
        // AccessCodes FK remains so revoked codes don't take their audit
        // trail with them.
        builder.HasOne(d => d.Photo)
            .WithMany()
            .HasForeignKey(d => d.PhotoId)
            .OnDelete(DeleteBehavior.Restrict);

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
