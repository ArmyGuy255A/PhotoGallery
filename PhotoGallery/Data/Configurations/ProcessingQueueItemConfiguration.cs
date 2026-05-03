using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

/// <summary>
/// Entity Framework configuration for ProcessingQueueItem.
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public class ProcessingQueueItemConfiguration : IEntityTypeConfiguration<ProcessingQueueItem>
{
    public void Configure(EntityTypeBuilder<ProcessingQueueItem> builder)
    {
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Id)
            .ValueGeneratedOnAdd();

        builder.Property(item => item.PhotoId)
            .IsRequired();

        builder.Property(item => item.ProcessingQueueId)
            .IsRequired();

        builder.Property(item => item.Quality)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(item => item.Status)
            .HasConversion<int>()
            .HasDefaultValue(ProcessingStatus.Pending);

        builder.Property(item => item.RetryCount)
            .HasDefaultValue(0);

        builder.Property(item => item.MaxRetries)
            .HasDefaultValue(3);

        builder.Property(item => item.LastError)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(item => item.Attempts)
            .HasDefaultValue(0);

        builder.Property(item => item.NextRetryTime)
            .IsRequired(false);

        builder.Property(item => item.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()")
            .IsRequired();

        builder.Property(item => item.CompletedAt)
            .IsRequired(false);

        builder.Property(item => item.UpdatedAt)
            .HasDefaultValueSql("GETUTCDATE()")
            .IsRequired();

        // Foreign keys
        builder.HasOne(item => item.Photo)
            .WithMany()
            .HasForeignKey(item => item.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(item => item.ProcessingQueue)
            .WithMany(pq => pq.Items)
            .HasForeignKey(item => item.ProcessingQueueId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes for common queries
        builder.HasIndex(item => item.ProcessingQueueId);
        builder.HasIndex(item => item.PhotoId);
        builder.HasIndex(item => item.Status);
        builder.HasIndex(item => item.Quality);
        builder.HasIndex(item => new { item.ProcessingQueueId, item.Quality }).IsUnique();
        
        // Filtered index for items ready to retry
        builder.HasIndex(item => new { item.Status, item.NextRetryTime })
            .HasFilter("[Status] = 3 AND [NextRetryTime] IS NOT NULL"); // Error status with retry time set
    }
}
