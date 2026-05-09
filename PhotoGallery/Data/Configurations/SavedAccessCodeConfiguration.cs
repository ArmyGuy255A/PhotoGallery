using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class SavedAccessCodeConfiguration : IEntityTypeConfiguration<SavedAccessCode>
{
    public void Configure(EntityTypeBuilder<SavedAccessCode> builder)
    {
        builder.HasKey(s => s.Id);

        builder.Property(s => s.UserId)
            .HasMaxLength(450)
            .IsRequired();

        builder.Property(s => s.SavedAt)
            .HasColumnType("datetime");

        builder.HasOne(s => s.AccessCode)
            .WithMany()
            .HasForeignKey(s => s.AccessCodeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.User)
            .WithMany()
            .HasForeignKey(s => s.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        // Idempotency: one save row per user+code
        builder.HasIndex(s => new { s.UserId, s.AccessCodeId }).IsUnique();
        builder.HasIndex(s => s.UserId);
    }
}
