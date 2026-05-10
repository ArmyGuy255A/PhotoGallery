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

    /// <summary>
    /// Non-generic ctor for derived contexts (e.g. <see cref="ApplicationDbContextSqlServer"/>)
    /// that carry their own typed <see cref="DbContextOptions{TContext}"/>. EF Core's
    /// constructor-resolution accepts the non-generic <see cref="DbContextOptions"/>
    /// fine as long as the subclass forwards its own typed options here.
    /// </summary>
    protected ApplicationDbContext(DbContextOptions options)
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
    public DbSet<AuditLogEntry> AuditLogEntries { get; set; }
    public DbSet<UserCartItem> UserCartItems { get; set; }
    
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
    }
}