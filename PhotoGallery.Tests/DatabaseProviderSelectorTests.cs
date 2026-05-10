using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PhotoGallery.Data;

namespace PhotoGallery.Tests;

/// <summary>
/// TDD coverage for <see cref="DatabaseProviderSelector"/>:
/// — `Database:Provider=Sqlite` (default) wires UseSqlite.
/// — `Database:Provider=SqlServer` wires UseSqlServer (Azure-backed dev path).
/// — Unknown values raise a clear error.
/// — Empty connection string is rejected upfront.
/// </summary>
public class DatabaseProviderSelectorTests
{
    private static IConfiguration BuildConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void Apply_DefaultsToSqlite_WhenNoProviderConfigured()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Data Source=app.db"
        });
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();

        DatabaseProviderSelector.Apply(options, config);

        Assert.Equal("Microsoft.EntityFrameworkCore.Sqlite",
            options.Options.Extensions
                .Select(e => e.GetType().Assembly.GetName().Name)
                .First(n => n!.Contains("Sqlite") || n.Contains("SqlServer")));
    }

    [Fact]
    public void Apply_SqlServer_WiresSqlServerExtension()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "SqlServer",
            ["ConnectionStrings:DefaultConnection"] = "Server=tcp:example.database.windows.net;Database=pg;"
        });
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();

        DatabaseProviderSelector.Apply(options, config);

        Assert.Contains(options.Options.Extensions,
            e => e.GetType().Assembly.GetName().Name == "Microsoft.EntityFrameworkCore.SqlServer");
    }

    [Fact]
    public void Apply_UnknownProvider_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Oracle",
            ["ConnectionStrings:DefaultConnection"] = "x"
        });
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseProviderSelector.Apply(options, config));
    }

    [Fact]
    public void Apply_EmptyConnectionString_Throws()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Database:Provider"] = "Sqlite"
        });
        var options = new DbContextOptionsBuilder<ApplicationDbContext>();

        Assert.Throws<InvalidOperationException>(() =>
            DatabaseProviderSelector.Apply(options, config));
    }
}
