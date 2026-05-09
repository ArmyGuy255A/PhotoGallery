using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Models;

namespace PhotoGallery.Data;

public class ApplicationDbContext : IdentityDbContext<User>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }
    
    public DbSet<Album> Albums { get; set; }
    public DbSet<Photo> Photos { get; set; }
    public DbSet<PhotoVersion> PhotoVersions { get; set; }
    public DbSet<PhotoFile> PhotoFiles { get; set; }
    public DbSet<AccessCode> AccessCodes { get; set; }
    public DbSet<UserAccessLog> UserAccessLogs { get; set; }
    public DbSet<ProcessingQueue> ProcessingQueues { get; set; }
    public DbSet<ProcessingQueueItem> ProcessingQueueItems { get; set; }
    public DbSet<PhotoVersionUrl> PhotoVersionUrls { get; set; }
    public DbSet<Download> Downloads { get; set; }
    public DbSet<SavedAccessCode> SavedAccessCodes { get; set; }
    
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}