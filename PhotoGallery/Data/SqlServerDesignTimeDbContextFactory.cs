using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PhotoGallery.Data;

/// <summary>
/// Design-time factory for <see cref="ApplicationDbContextSqlServer"/>.
///
/// EF Core tooling (<c>dotnet ef migrations add</c>, <c>dotnet ef migrations script</c>,
/// etc.) invokes this factory to construct the context without booting the
/// full application host. The connection string is a build-time placeholder —
/// scaffolding does not require a reachable database, only a valid syntactic
/// connection string for the provider to parse.
///
/// Override per developer with the <c>EFCORE_SQLSERVER_DESIGNTIME_CONNECTION</c>
/// environment variable if needed (e.g., to point at LocalDB for `dotnet ef
/// dbcontext info` introspection); the default value is sufficient for
/// scaffolding migrations.
/// </summary>
public class SqlServerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContextSqlServer>
{
    private const string DefaultDesignTimeConnection =
        "Server=tcp:design-time.invalid,1433;Database=PhotoGalleryDesignTime;" +
        "User Id=design;Password=design;TrustServerCertificate=true;";

    public ApplicationDbContextSqlServer CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("EFCORE_SQLSERVER_DESIGNTIME_CONNECTION")
                               ?? DefaultDesignTimeConnection;

        var options = new DbContextOptionsBuilder<ApplicationDbContextSqlServer>()
            .UseSqlServer(connectionString, sql =>
            {
                // Migrations for this context live in a dedicated namespace +
                // folder so they coexist with the Sqlite migrations bound to
                // the base ApplicationDbContext.
                sql.MigrationsAssembly(typeof(ApplicationDbContextSqlServer).Assembly.GetName().Name);
            })
            .Options;

        return new ApplicationDbContextSqlServer(options);
    }
}
