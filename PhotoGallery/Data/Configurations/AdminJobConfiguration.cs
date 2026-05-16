using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class AdminJobConfiguration : IEntityTypeConfiguration<AdminJob>
{
    public void Configure(EntityTypeBuilder<AdminJob> builder)
    {
        builder.HasKey(j => j.Id);
        // Worker poll path: select pending rows by (JobType, Status, RequestedAt).
        builder.HasIndex(j => new { j.JobType, j.Status });
        builder.HasIndex(j => j.RequestedAt);
    }
}
