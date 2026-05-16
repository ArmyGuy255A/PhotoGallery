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

        // Must match AspNetUsers.Id (nvarchar(450), the IdentityUser<string>
        // default) because UserConfiguration declares
        // `HasMany(u => u.AccessCodes).WithOne().HasForeignKey(ac => ac.CreatedBy)`.
        // SqlServer enforces "FK column length == principal key length"; a
        // mismatched 256 vs 450 fails the InitialCreate migration on its
        // first run with "Column 'AspNetUsers.Id' is not the same length or
        // scale as referencing column 'AccessCodes.CreatedBy' in foreign
        // key 'FK_AccessCodes_AspNetUsers_CreatedBy'."
        builder.Property(ac => ac.CreatedBy)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(ac => ac.RowVersion)
            .HasDefaultValue(new byte[] { 1 });

        // Snapshot of the album title at the moment of soft-delete. 200 char
        // ceiling matches AlbumConfiguration's title max length.
        builder.Property(ac => ac.DeletedAlbumTitle)
            .HasMaxLength(200);

        // Album FK is nullable + SetNull on delete: when an admin / owner
        // hard-deletes an album, the SetNull cascade lets the AccessCode row
        // survive for analytics. The AlbumsController.DeleteAlbum path also
        // sets IsDeleted / DeletedAt / DeletedAlbumTitle on each code in
        // the same transaction so the analytics view still has a name.
        builder.HasOne(ac => ac.Album)
            .WithMany(a => a.AccessCodes)
            .HasForeignKey(ac => ac.AlbumId)
            .OnDelete(DeleteBehavior.SetNull)
            .IsRequired(false);

        builder.HasMany(ac => ac.UserAccessLogs)
            .WithOne(ual => ual.AccessCode)
            .HasForeignKey(ual => ual.AccessCodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ac => ac.Code).IsUnique();
        builder.HasIndex(ac => ac.AlbumId);
        builder.HasIndex(ac => ac.ExpirationDate);
        builder.HasIndex(ac => ac.IsDeleted);
    }
}
