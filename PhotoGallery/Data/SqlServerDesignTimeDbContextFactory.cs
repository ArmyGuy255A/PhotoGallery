using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PhotoGallery.Data;

/// <summary>
/// Design-time factory for <see cref="ApplicationDbContextSqlServer"/>.
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
///   dotnet ef database update --context ApplicationDbContextSqlServer
/// </code>
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
            // Mirror the runtime suppression from DatabaseProviderSelector so
            // `dotnet ef database update` doesn't fail with the EF Core 9
            // PendingModelChangesWarning when the model fingerprint and the
            // latest migration's snapshot differ in trivia (cross-host
            // scaffolding differences, etc.). Real schema mismatches still
            // surface as SqlExceptions on the actual DDL.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return new ApplicationDbContextSqlServer(options);
    }
}
