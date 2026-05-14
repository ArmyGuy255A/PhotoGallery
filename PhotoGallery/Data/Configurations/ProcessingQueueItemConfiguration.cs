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

        builder.Property(item => item.LastError)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.Property(item => item.Attempts)
            .HasDefaultValue(0);

        builder.Property(item => item.NextRetryTime)
            .IsRequired(false);

        builder.Property(item => item.LeaseExpiresAt)
            .IsRequired(false);

        builder.Property(item => item.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()")
            .IsRequired();

        builder.Property(item => item.CompletedAt)
            .IsRequired(false);

        builder.Property(item => item.UpdatedAt)
            .HasDefaultValueSql("GETUTCDATE()")
            .IsRequired();

        // SqlServer rejects multi-cascade paths to ProcessingQueueItems
        // (Photos → Items via PhotoId, plus ProcessingQueues → Items via
        // ProcessingQueueId, both reachable from User on a delete). Restrict
        // the Photo path; app code already clears queue items when purging
        // a Photo. The ProcessingQueue → Items cascade stays so destroying
        // a queue takes its items with it.
        builder.HasOne(item => item.Photo)
            .WithMany()
            .HasForeignKey(item => item.PhotoId)
            .OnDelete(DeleteBehavior.Restrict);

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

        // Lease index — supports the Phase 4 §4 dequeue query that picks rows where
        // Status indicates work to do AND the lease is either unset or expired.
        builder.HasIndex(item => new { item.Status, item.LeaseExpiresAt });
    }
}
