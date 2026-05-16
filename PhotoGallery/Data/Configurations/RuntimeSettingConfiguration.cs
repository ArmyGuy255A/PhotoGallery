using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PhotoGallery.Models;

namespace PhotoGallery.Data.Configurations;

public class RuntimeSettingConfiguration : IEntityTypeConfiguration<RuntimeSetting>
{
    public void Configure(EntityTypeBuilder<RuntimeSetting> b)
    {
        b.HasKey(x => x.Id);
        b.Property(x => x.Key).HasMaxLength(200).IsRequired();
        b.Property(x => x.Value).HasMaxLength(2000).IsRequired();
        b.Property(x => x.Category).HasMaxLength(100).IsRequired();
        b.Property(x => x.DataType).HasMaxLength(20).IsRequired();
        b.Property(x => x.Description).HasMaxLength(500);
        b.Property(x => x.LastModifiedBy).HasMaxLength(450);
        b.HasIndex(x => x.Key).IsUnique();
        b.HasIndex(x => x.Category);
    }
}
