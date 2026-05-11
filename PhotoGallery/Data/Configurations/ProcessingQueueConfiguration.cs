using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

/// <summary>
/// Entity Framework configuration for ProcessingQueue.
/// Reference: D003 (Image Processing with Compression Profiles)
/// </summary>
public class ProcessingQueueConfiguration : IEntityTypeConfiguration<ProcessingQueue>
{
    public void Configure(EntityTypeBuilder<ProcessingQueue> builder)
    {
        builder.HasKey(pq => pq.Id);

        builder.Property(pq => pq.Id)
            .ValueGeneratedOnAdd();

        builder.Property(pq => pq.PhotoId)
            .IsRequired();

        builder.Property(pq => pq.Status)
            .HasConversion<int>()
            .HasDefaultValue(ProcessingStatus.Pending);

        builder.Property(pq => pq.CreatedAt)
            .HasDefaultValueSql("GETUTCDATE()")
            .IsRequired();

        builder.Property(pq => pq.CompletedAt)
            .IsRequired(false);

        builder.Property(pq => pq.ErrorMessage)
            .HasMaxLength(500)
            .IsRequired(false);

        // SqlServer rejects this cascade chain because:
        //   Photo → ProcessingQueue (cascade) → ProcessingQueueItems (cascade)
        //   ProcessingQueueItem.PhotoId references Photo too (now Restrict)
        // The schema-level cycle (Photo ↔ ProcessingQueue ↔ Items) is enough
        // for SqlServer's conservative check to fail. Restrict the Photo edge
        // so a Photo can't be deleted while a queue references it — the
        // image-processing service is responsible for clearing the queue
        // before purging.
        builder.HasOne(pq => pq.Photo)
            .WithMany()
            .HasForeignKey(pq => pq.PhotoId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(pq => pq.Items)
            .WithOne(item => item.ProcessingQueue)
            .HasForeignKey(item => item.ProcessingQueueId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes for common queries
        builder.HasIndex(pq => pq.Status);
        builder.HasIndex(pq => pq.PhotoId).IsUnique();
        builder.HasIndex(pq => pq.CreatedAt);
        builder.HasIndex(pq => pq.Status)
            // SqlServer filtered indexes don't support OR; use a range
            // comparison instead. Status enum: Pending=0, Processing=1.
            .HasFilter("[Status] < 2"); // Pending or Processing
    }
}
