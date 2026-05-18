using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhotoGallery.Models;

namespace PhotoGallery.Data;

public static class InitializerExtensions
{
    public static async Task InitializeDatabaseAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();

        var initializer = scope.ServiceProvider.GetRequiredService<ApplicationDbContextInitializer>();

        await initializer.InitializeAsync();

        await initializer.SeedAsync();
    }
}

public class ApplicationDbContextInitializer(
    ILogger<ApplicationDbContextInitializer> logger,
    ApplicationDbContext context,
    UserManager<User> userManager,
    RoleManager<IdentityRole> roleManager,
    IConfiguration configuration)
{
    public async Task InitializeAsync()
    {
        try
        {
            // MigrateAsync only works on a relational provider (SqlServer in
            // dev/Trial/Prod). The xUnit BasePath integration tests swap the
            // context onto the EF Core InMemory provider, where MigrateAsync
            // throws InvalidOperationException. Use EnsureCreatedAsync as the
            // schema-bootstrap path for non-relational providers — this is a
            // test seam only; production always hits the relational branch.
            if (context.Database.IsRelational())
            {
                await context.Database.MigrateAsync();
            }
            else
            {
                await context.Database.EnsureCreatedAsync();
            }
        }
        catch (Exception ex)
        {
            // RETHROW — startup must NOT silently continue with an
            // un-migrated database. Symptoms of swallowing this previously
            // were a healthy container that 500'd every request because
            // `dbo.AspNetUsers` (and every other table) didn't exist.
            // Program.cs catches at the top level, logs the fatal, and
            // aborts the process so the container app's readiness probe
            // fails fast and the deployment is flagged.
            logger.LogError(ex, "An error occurred while initialising the database.");
            throw;
        }
    }

    public async Task SeedAsync()
    {
        try
        {
            await TrySeedAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    public async Task TrySeedAsync()
    {
        // Create roles if they don't exist
        var adminRole = new IdentityRole(Roles.Admin);
        if (roleManager.Roles.All(r => r.Name != adminRole.Name))
        {
            await roleManager.CreateAsync(adminRole);
        }

        var userRole = new IdentityRole(Roles.User);
        if (roleManager.Roles.All(r => r.Name != userRole.Name))
        {
            await roleManager.CreateAsync(userRole);
        }

        // AlbumCreator: elevated, non-admin role for users who can create albums + upload photos.
        var albumCreatorRole = new IdentityRole(Roles.AlbumCreator);
        if (roleManager.Roles.All(r => r.Name != albumCreatorRole.Name))
        {
            await roleManager.CreateAsync(albumCreatorRole);
        }

        // Seed admin user: mrdieppa@gmail.com
        var adminEmail = configuration["Auth:AdminEmail"] ?? "mrdieppa@gmail.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        if (adminUser == null)
        {
            adminUser = new User
            {
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true,
                CreatedDate = DateTime.UtcNow,
                IsActive = true
            };

            await userManager.CreateAsync(adminUser);
            await userManager.AddToRoleAsync(adminUser, Roles.Admin);
            logger.LogInformation($"Admin user created: {adminEmail}");
        }
        else if (!await userManager.IsInRoleAsync(adminUser, Roles.Admin))
        {
            await userManager.AddToRoleAsync(adminUser, Roles.Admin);
            logger.LogInformation($"Admin role added to user: {adminEmail}");
        }

        // Seed test user if DISABLE_AUTH is enabled
        var disableAuth = configuration["DISABLE_AUTH"] ?? "false";
        if (disableAuth.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            const string testEmail = "testadmin@localhost";
            var testUser = await userManager.FindByEmailAsync(testEmail);
            if (testUser == null)
            {
                testUser = new User
                {
                    UserName = testEmail,
                    Email = testEmail,
                    EmailConfirmed = true,
                    CreatedDate = DateTime.UtcNow,
                    IsActive = true
                };

                await userManager.CreateAsync(testUser);
                await userManager.AddToRoleAsync(testUser, Roles.Admin);
                logger.LogInformation($"Test user created: {testEmail}");
            }
            else if (!await userManager.IsInRoleAsync(testUser, Roles.Admin))
            {
                await userManager.AddToRoleAsync(testUser, Roles.Admin);
                logger.LogInformation($"Admin role added to test user: {testEmail}");
            }
        }

        await context.SaveChangesAsync();
    }
}