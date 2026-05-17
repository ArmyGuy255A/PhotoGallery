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

        // Defence-in-depth: even if the controller-level duplicate check
        // is bypassed (race between two requests, future code path that
        // forgets to call it), the database refuses a second photo with
        // the same FileName in the same AlbumId. The filter excludes
        // PhotoProcessingStatus.Uploading (=4) so an abandoned ticket
        // does not permanently block re-attempts of the same name. The
        // OrphanedBlobReaperService eventually deletes those rows.
        //
        // SqlServer honours the HasFilter clause as a filtered unique index.
        builder.HasIndex(p => new { p.AlbumId, p.FileName })
            .IsUnique()
            .HasFilter("[ProcessingStatus] <> 4");
    }
}
