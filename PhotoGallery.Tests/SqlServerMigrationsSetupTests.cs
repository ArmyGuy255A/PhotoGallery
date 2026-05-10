using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PhotoGallery.Data;

namespace PhotoGallery.Tests;

/// <summary>
/// TDD coverage for the dual-migration setup:
/// — <see cref="ApplicationDbContextSqlServer"/> is a real subclass of
///   <see cref="ApplicationDbContext"/> so DI consumers that inject the
///   base type keep working after the SqlServer switch.
/// — <see cref="SqlServerDesignTimeDbContextFactory"/> produces a working
///   context for EF tooling without booting the application host.
/// — The SqlServer-flavored migrations live under the expected namespace
///   and are bound to the subclass type (so EF's runtime applies the right
///   set per provider).
/// </summary>
public class SqlServerMigrationsSetupTests
{
    [Fact]
    public void ApplicationDbContextSqlServer_IsSubclassOf_ApplicationDbContext()
    {
        Assert.True(typeof(ApplicationDbContext).IsAssignableFrom(typeof(ApplicationDbContextSqlServer)),
            "ApplicationDbContextSqlServer must inherit from ApplicationDbContext so DI consumers " +
            "of the base type get the SqlServer-backed instance via forwarding registration.");
    }

    [Fact]
    public void DesignTimeFactory_ProducesContextWithSqlServerProvider()
    {
        var factory = new SqlServerDesignTimeDbContextFactory();
        using var context = factory.CreateDbContext(Array.Empty<string>());

        Assert.IsType<ApplicationDbContextSqlServer>(context);
        // The provider name is exposed via Database.ProviderName.
        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
    }

    [Fact]
    public void SqlServerInitialCreateMigration_IsBoundToSqlServerContext()
    {
        // Locates the scaffolded migration class and verifies its
        // [DbContext(typeof(ApplicationDbContextSqlServer))] attribute.
        // If EF accidentally regenerated this against ApplicationDbContext,
        // the runtime would try to apply Sqlite-flavored DDL to SqlServer.
        var assembly = typeof(ApplicationDbContextSqlServer).Assembly;
        var migrationType = assembly.GetTypes()
            .FirstOrDefault(t =>
                t.Namespace == "PhotoGallery.Data.Migrations.SqlServer" &&
                t.GetCustomAttribute<MigrationAttribute>()?.Id.EndsWith("_InitialCreate") == true);

        Assert.NotNull(migrationType);
        var dbContextAttr = migrationType!.GetCustomAttribute<DbContextAttribute>();
        Assert.NotNull(dbContextAttr);
        Assert.Equal(typeof(ApplicationDbContextSqlServer), dbContextAttr!.ContextType);
    }

    [Fact]
    public void SqliteMigrations_RemainBoundTo_BaseApplicationDbContext()
    {
        // Regression-guard: the dual-context refactor must not have nudged
        // the existing Sqlite migrations onto the subclass. The existing
        // migration history table on Sqlite databases is keyed by the
        // base context type — moving them would re-trigger every migration
        // on next startup and could corrupt user data.
        var assembly = typeof(ApplicationDbContext).Assembly;
        var sqliteMigrations = assembly.GetTypes()
            .Where(t =>
                t.Namespace == "PhotoGallery.Data.Migrations" &&
                t.GetCustomAttribute<MigrationAttribute>() != null)
            .ToList();

        Assert.NotEmpty(sqliteMigrations);
        foreach (var migration in sqliteMigrations)
        {
            var dbContextAttr = migration.GetCustomAttribute<DbContextAttribute>();
            Assert.NotNull(dbContextAttr);
            Assert.Equal(typeof(ApplicationDbContext), dbContextAttr!.ContextType);
        }
    }
}
