using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PhotoGallery.Data;

/// <summary>
/// Design-time factory for <see cref="ApplicationDbContext"/>.
///
/// EF Core tooling (<c>dotnet ef migrations add</c>, <c>dotnet ef migrations script</c>,
/// <c>dotnet ef database update</c>, etc.) invokes this factory to construct
/// the context without booting the full application host. The connection
/// string is a build-time placeholder by default — scaffolding does not
/// require a reachable database, only a valid syntactic connection string
/// for the provider to parse.
///
/// Override per developer with the <c>EFCORE_SQLSERVER_DESIGNTIME_CONNECTION</c>
/// environment variable to point at a real DB (e.g. for <c>dotnet ef database
/// update</c> against Azure SQL):
///
/// <code>
///   $env:EFCORE_SQLSERVER_DESIGNTIME_CONNECTION = "Server=tcp:...;Database=photogallery;Authentication=Active Directory Default;Encrypt=True;"
///   dotnet ef database update
/// </code>
/// </summary>
public class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string DefaultDesignTimeConnection =
        "Server=tcp:design-time.invalid,1433;Database=PhotoGalleryDesignTime;" +
        "User Id=design;Password=design;TrustServerCertificate=true;";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("EFCORE_SQLSERVER_DESIGNTIME_CONNECTION")
                               ?? DefaultDesignTimeConnection;

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.GetName().Name);
            })
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new ApplicationDbContext(options);
    }
}

