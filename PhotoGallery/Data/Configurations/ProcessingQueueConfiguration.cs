using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class ProcessingQueueConfiguration : IEntityTypeConfiguration<ProcessingQueue>
{
    public void Configure(EntityTypeBuilder<ProcessingQueue> builder)
    {
        builder.HasKey(pq => pq.Id);

        builder.Property(pq => pq.Id)
            .HasMaxLength(36);

        builder.Property(pq => pq.PhotoId);

        builder.Property(pq => pq.Status)
            .HasConversion<int>()
            .HasDefaultValue(ProcessingStatus.Pending);

        builder.Property(pq => pq.ErrorMessage)
            .HasMaxLength(500);

        builder.HasOne(pq => pq.Photo)
            .WithMany()
            .HasForeignKey(pq => pq.PhotoId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(pq => pq.Status);
        builder.HasIndex(pq => pq.PhotoId).IsUnique();
        builder.HasIndex(pq => pq.QueuedDate);
    }
}
