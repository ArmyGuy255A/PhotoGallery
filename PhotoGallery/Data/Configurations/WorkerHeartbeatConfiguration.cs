using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class WorkerHeartbeatConfiguration : IEntityTypeConfiguration<WorkerHeartbeat>
{
    public void Configure(EntityTypeBuilder<WorkerHeartbeat> builder)
    {
        builder.HasKey(h => h.Id);
        builder.HasIndex(h => new { h.WorkerName, h.InstanceId }).IsUnique();
        builder.HasIndex(h => h.LastHeartbeatAt);
    }
}
