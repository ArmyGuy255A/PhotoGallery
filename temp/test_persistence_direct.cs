using Microsoft.EntityFrameworkCore;
using PhotoGallery.Data;
using PhotoGallery.Data.Repositories;
using PhotoGallery.Models;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

// Add DbContext with same config as app
var connectionString = "DataSource=test_persistence.db;Cache=Shared";
services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlite(connectionString, sqliteOptions =>
    {
        sqliteOptions.CommandTimeout(5);
    });
});

services.AddScoped(typeof(IRepository<>), typeof(Repository<>));
services.AddScoped<IAlbumRepository, AlbumRepository>();

var serviceProvider = services.BuildServiceProvider();

using (var scope = serviceProvider.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    
    // Ensure database is created
    await context.Database.EnsureCreatedAsync();
    Console.WriteLine("Database created/verified");
    
    // Check existing albums
    var existingCount = await context.Albums.CountAsync();
    Console.WriteLine($"Existing albums: {existingCount}");
    
    // Create an album
    var album = new Album
    {
        Id = Guid.NewGuid(),
        Title = "Test Album Direct",
        Description = "Testing persistence directly",
        OwnerId = "test-user",
        CreatedBy = "test-user",
        CreatedDate = DateTime.UtcNow
    };
    
    Console.WriteLine($"Adding album: {album.Id}");
    context.Albums.Add(album);
    
    Console.WriteLine("Calling SaveChangesAsync...");
    var changes = await context.SaveChangesAsync();
    Console.WriteLine($"SaveChangesAsync returned: {changes}");
    
    // Immediately query
    var retrieved = await context.Albums.FirstOrDefaultAsync(a => a.Id == album.Id);
    if (retrieved != null)
    {
        Console.WriteLine($"✅ Album found immediately after save: {retrieved.Title}");
    }
    else
    {
        Console.WriteLine($"❌ Album NOT found after save!");
    }
    
    // Check count
    var newCount = await context.Albums.CountAsync();
    Console.WriteLine($"Album count after insert: {newCount}");
}

// Create a NEW scope to test if it persisted across scopes
Console.WriteLine("\nTesting with new scope...");
using (var scope = serviceProvider.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    
    var allAlbums = await context.Albums.ToListAsync();
    Console.WriteLine($"Albums in new scope: {allAlbums.Count}");
    foreach (var a in allAlbums)
    {
        Console.WriteLine($"  - {a.Title} (ID: {a.Id}, OwnerId: {a.OwnerId})");
    }
}
