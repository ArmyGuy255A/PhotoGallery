using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class UserCartItemConfiguration : IEntityTypeConfiguration<UserCartItem>
{
    public void Configure(EntityTypeBuilder<UserCartItem> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(c => c.AddedAt)
            .HasColumnType("datetime");

        builder.Property(c => c.Quality)
            .IsRequired();

        builder.HasOne(c => c.User)
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(c => c.Photo)
            .WithMany()
            .HasForeignKey(c => c.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Soft FK to Album — never blocks album delete; row's SourceAlbumId is set NULL.
        builder.HasOne(c => c.SourceAlbum)
            .WithMany()
            .HasForeignKey(c => c.SourceAlbumId)
            .OnDelete(DeleteBehavior.SetNull);

        // Idempotent add: at most one row per (User, Photo, Quality).
        builder.HasIndex(c => new { c.UserId, c.PhotoId, c.Quality }).IsUnique();

        // Cart-list query optimization.
        builder.HasIndex(c => c.UserId);
    }
}
